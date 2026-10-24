using System.Globalization;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Nyra.WebSocket;

namespace Nyra.Internal;

internal sealed class NodeConnection : IDisposable
{
    private const int LoggedAttemptsBurst = 10;

    private const int LoggedAttemptsInterval = 30;

    private readonly Node _node;
    private readonly NodeEventDispatcher _dispatcher;
    private readonly Func<CancellationToken, Task> _preConnect;
    private readonly Lock _lock = new();

    private WiredClient? _wired;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _supervisionTask;
    private TaskCompletionSource _disconnectSignal = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private (
        WebSocketCloseStatus? Status,
        string? Description,
        Exception? Exception
    ) _lastCloseInfo;
    private Exception? _initialConnectException;
    private volatile bool _stopped;
    private bool _everConnected;
    private int _reconnectAttempts;

    private int _generation;

    private volatile int _latestGeneration;

    internal int LatestGeneration => _latestGeneration;

    private volatile int _publishedConnectionEpoch = -1;

    private sealed record WiredClient(
        WebSocketClient Client,
        Action<ReadOnlyMemory<byte>> TextHandler,
        Action<WebSocketCloseStatus?, string?, Exception?> ClosedHandler
    )
    {
        public void Detach()
        {
            Client.TextMessageReceived -= TextHandler;
            Client.Closed -= ClosedHandler;
        }
    }

    public NodeConnection(
        Node node,
        NodeEventDispatcher dispatcher,
        Func<CancellationToken, Task> preConnect
    )
    {
        _node = node;
        _dispatcher = dispatcher;
        _preConnect = preConnect;
    }

    public bool IsConnected
    {
        get
        {
            lock (_lock)
                return _wired?.Client.IsConnected ?? false;
        }
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource lifetimeCts;
        CancellationToken lifetimeToken;
        TaskCompletionSource disconnectSignal;
        int generation;
        lock (_lock)
        {
            if (_supervisionTask is not null)
                return false;

            _stopped = false;
            _reconnectAttempts = 0;
            _everConnected = false;
            _initialConnectException = null;
            _lastCloseInfo = default;
            generation = NextGeneration();
            _disconnectSignal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            disconnectSignal = _disconnectSignal;
            lifetimeCts = new CancellationTokenSource();
            _lifetimeCts = lifetimeCts;
            lifetimeToken = lifetimeCts.Token;
            _supervisionTask = SupervisionLoopAsync(generation, lifetimeToken);
        }

        try
        {
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lifetimeToken
            );
            await ConnectOnceAsync(generation, attemptCts.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            lock (_lock)
                if (_generation == generation)
                    _initialConnectException = exception;
            disconnectSignal.TrySetResult();
            throw;
        }

        return true;
    }

    internal void NoteReady()
    {
        lock (_lock)
            _reconnectAttempts = 0;
    }

    internal bool IsCurrentGeneration(int generation)
    {
        lock (_lock)
            return _generation == generation;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? supervisionTask;
        WiredClient? wired;
        CancellationTokenSource? lifetimeCts;
        TaskCompletionSource disconnectSignal;

        lock (_lock)
        {
            _stopped = true;
            _everConnected = false;
            _lastCloseInfo = default;
            NextGeneration();
            supervisionTask = _supervisionTask;
            _supervisionTask = null;
            wired = _wired;
            _wired = null;
            lifetimeCts = _lifetimeCts;
            _lifetimeCts = null;
            disconnectSignal = _disconnectSignal;
        }

        if (wired is not null)
            await StopClientAsync(wired, cancellationToken).ConfigureAwait(false);

        TryCancel(lifetimeCts);

        disconnectSignal.TrySetResult();

        if (supervisionTask is not null)
        {
            await supervisionTask
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (!supervisionTask.IsCompleted)
                supervisionTask.ObserveFaults();
        }

        lifetimeCts?.Dispose();
    }

    public async Task AbortCurrentConnectionAsync(
        string reason,
        Exception exception,
        int expectedEpoch
    )
    {
        WiredClient? wired;
        TaskCompletionSource disconnectSignal;

        lock (_lock)
        {
            wired = _wired;
            if (wired is null || !wired.Client.IsConnected)
                return;

            if (expectedEpoch != _publishedConnectionEpoch)
                return;
            _wired = null;

            _lastCloseInfo = (null, reason, exception);
            disconnectSignal = _disconnectSignal;
        }

        try
        {
            await StopClientAsync(wired, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception stopException)
        {
            _node.HandleError(stopException);
        }

        disconnectSignal.TrySetResult();
    }

    public void Dispose()
    {
        WiredClient? wired;
        TaskCompletionSource disconnectSignal;

        lock (_lock)
        {
            _stopped = true;
            NextGeneration();
            TryCancel(_lifetimeCts);
            _lifetimeCts?.Dispose();
            _lifetimeCts = null;

            wired = _wired;
            _wired = null;
            _supervisionTask = null;
            disconnectSignal = _disconnectSignal;
        }

        wired?.Detach();
        wired?.Client.Dispose();

        disconnectSignal.TrySetResult();
    }

    private async Task<bool> ConnectOnceAsync(int generation, CancellationToken lifetimeCt)
    {
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCt);
        probeCts.CancelAfter(_node.Options.ReadyTimeout);
        try
        {
            await _preConnect(probeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (probeCts.IsCancellationRequested && !lifetimeCt.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The server at {_node.Endpoint} did not answer the pre-connect probe within {_node.Options.ReadyTimeout}."
            );
        }

        var client = new WebSocketClient();

        var connectionEpoch = 0;
        void OnText(ReadOnlyMemory<byte> data) =>
            HandleTextMessage(generation, connectionEpoch, data);
        void OnClosed(
            WebSocketCloseStatus? closeStatus,
            string? closeDescription,
            Exception? exception
        ) => HandleClosed(generation, closeStatus, closeDescription, exception);
        var wired = new WiredClient(client, OnText, OnClosed);

        try
        {
            client.TextMessageReceived += OnText;
            client.Closed += OnClosed;
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCt);
            handshakeCts.CancelAfter(_node.Options.ReadyTimeout);
            try
            {
                await client
                    .ConnectAsync(
                        _node.Endpoint.ToWebSocketUri(),
                        BuildHeaders(),
                        handshakeCts.Token
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (handshakeCts.IsCancellationRequested && !lifetimeCt.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"The server at {_node.Endpoint} did not complete the websocket upgrade within {_node.Options.ReadyTimeout}."
                );
            }
        }
        catch
        {
            wired.Detach();
            client.Dispose();
            throw;
        }

        WiredClient? previous = null;
        var discarded = false;
        lock (_lock)
            if (_stopped || _generation != generation)
            {
                wired.Detach();
                discarded = true;
            }
            else
            {
                previous = _wired;
                _wired = wired;
                _publishedConnectionEpoch = -1;
                _everConnected = true;
            }

        if (discarded)
        {
            client.Dispose();
            return false;
        }

        if (previous is not null)
            await StopClientAsync(previous, CancellationToken.None).ConfigureAwait(false);

        lock (_lock)
            if (_stopped || _generation != generation)
                return true;

        connectionEpoch = _node.EventHandlers.HandleConnected(generation);
        if (connectionEpoch < 0)
        {
            wired.Detach();
            client.Dispose();
            return false;
        }

        var poisonedTail = false;
        lock (_lock)
        {
            if (_stopped || _generation != generation)
            {
                wired.Detach();
                poisonedTail = true;
            }
            else
                _publishedConnectionEpoch = connectionEpoch;
        }

        if (poisonedTail)
        {
            client.Dispose();
            return false;
        }

        _node.EventHandlers.ArmReadyWatchdog(connectionEpoch, generation);

        client.StartReceiving();
        return true;
    }

    private async Task StopClientAsync(WiredClient wired, CancellationToken cancellationToken)
    {
        wired.Detach();
        try
        {
            await wired.Client.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            wired.Client.Dispose();
        }
    }

    private static void TryCancel(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    private async Task SupervisionLoopAsync(int generation, CancellationToken lifetimeCt)
    {
        try
        {
            while (!lifetimeCt.IsCancellationRequested)
            {
                TaskCompletionSource signal;
                lock (_lock)
                    signal = _disconnectSignal;
                await signal.Task.WaitAsync(lifetimeCt).ConfigureAwait(false);
                WebSocketCloseStatus? closeStatus;
                string? closeDescription;
                Exception? closeException;
                Exception? initialConnectException;
                bool wasEverConnected;
                lock (_lock)
                {
                    if (_generation != generation)
                        return;

                    _disconnectSignal = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );

                    (closeStatus, closeDescription, closeException) = _lastCloseInfo;
                    initialConnectException = _initialConnectException;
                    wasEverConnected = _everConnected;
                }

                if (_stopped)
                    return;

                if (wasEverConnected)
                {
                    try
                    {
                        _node.Logger?.LogWarning(
                            LogEvents.Node,
                            closeException,
                            "Node {NodeName} disconnected ({CloseStatus} {CloseDescription}).",
                            _node.Name,
                            closeStatus,
                            closeDescription
                        );
                    }
                    catch { }
                    _node.EventHandlers.HandleDisconnected(
                        closeStatus,
                        closeDescription,
                        closeException,
                        final: false,
                        ownerGeneration: generation
                    );
                }
                else
                    ConnectionFailure.Log(
                        _node.Logger,
                        _node.Name,
                        _node.Endpoint,
                        initial: true,
                        initialConnectException
                            ?? closeException
                            ?? new InvalidOperationException(
                                "The websocket closed before the connection was established."
                            )
                    );

                Exception? lastConnectFailure = null;

                while (!lifetimeCt.IsCancellationRequested && !_stopped)
                {
                    var attempt = Interlocked.Increment(ref _reconnectAttempts);
                    if (
                        _node.Options.MaxReconnectAttempts >= 0
                        && attempt > _node.Options.MaxReconnectAttempts
                    )
                    {
                        try
                        {
                            _node.Logger?.LogError(
                                LogEvents.Node,
                                "Node {NodeName} exceeded the maximum of {MaxAttempts} reconnect attempts, giving up.",
                                _node.Name,
                                _node.Options.MaxReconnectAttempts
                            );
                        }
                        catch { }
                        EndSupervision(generation);
                        if (_generation != generation)
                            return;
                        _node.EventHandlers.HandleDisconnected(
                            closeStatus,
                            closeDescription,
                            lastConnectFailure ?? initialConnectException ?? closeException,
                            final: true,
                            ownerGeneration: generation
                        );
                        return;
                    }

                    var delay = ComputeReconnectDelay(attempt);
                    var logAttempt = ShouldLogAttempt(attempt);
                    if (logAttempt)
                        try
                        {
                            _node.Logger?.LogInformation(
                                LogEvents.Node,
                                wasEverConnected
                                    ? "Node {NodeName} reconnecting in {Delay} (attempt {Attempt})."
                                    : "Node {NodeName} connecting in {Delay} (attempt {Attempt}).",
                                _node.Name,
                                TimeSpan.FromSeconds(Math.Round(delay.TotalSeconds)),
                                _node.Options.MaxReconnectAttempts < 0
                                    ? attempt.ToString()
                                    : $"{attempt}/{_node.Options.MaxReconnectAttempts}"
                            );
                        }
                        catch { }

                    try
                    {
                        await Task.Delay(delay, lifetimeCt).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    try
                    {
                        if (!await ConnectOnceAsync(generation, lifetimeCt).ConfigureAwait(false))
                        {
                            EndSupervision(generation);
                            return;
                        }

                        try
                        {
                            _node.Logger?.LogInformation(
                                LogEvents.Node,
                                wasEverConnected
                                    ? "Node {NodeName} reconnected."
                                    : "Node {NodeName} connected.",
                                _node.Name
                            );
                        }
                        catch { }
                        break;
                    }
                    catch (OperationCanceledException) when (lifetimeCt.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        lastConnectFailure = exception;
                        if (logAttempt)
                            ConnectionFailure.Log(
                                _node.Logger,
                                _node.Name,
                                _node.Endpoint,
                                initial: !wasEverConnected,
                                exception
                            );
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            TaskCompletionSource<bool>? loopCompletion = null;
            WiredClient? wired = null;
            lock (_node.EventsLock)
            {
                bool ownsGeneration;
                lock (_lock)
                    ownsGeneration = _generation == generation;

                if (ownsGeneration)
                {
                    loopCompletion = _node.TakeConnectCompletion(generation);
                    wired = _wired;
                    _wired = null;
                }
            }

            if (wired is not null)
                try
                {
                    await StopClientAsync(wired, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception stopException)
                {
                    _node.HandleError(stopException);
                }

            EndSupervision(generation);
            loopCompletion?.TrySetResult(false);
            _node.HandleError(exception);

            if (wired is not null && _generation == generation)
                _node.EventHandlers.HandleDisconnected(
                    null,
                    "the connection's supervision loop failed",
                    exception,
                    final: true,
                    ownerGeneration: generation
                );
        }
    }

    private int NextGeneration() => _latestGeneration = ++_generation;

    private void EndSupervision(int generation)
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            if (_generation != generation)
                return;

            cts = _lifetimeCts;
            _supervisionTask = null;
            _lifetimeCts = null;
        }

        cts?.Dispose();
    }

    private static bool ShouldLogAttempt(int attempt) =>
        attempt <= LoggedAttemptsBurst || attempt % LoggedAttemptsInterval == 0;

    private TimeSpan ComputeReconnectDelay(int attempt)
    {
        var exponent = Math.Min(attempt - 1, 62);
        var delay = _node.Options.ReconnectDelay.TotalMilliseconds * Math.Pow(2, exponent);
        delay *= 0.8 + (Random.Shared.NextDouble() * 0.4);
        return TimeSpan.FromMilliseconds(
            Math.Max(1.0, Math.Min(delay, _node.Options.MaxReconnectDelay.TotalMilliseconds))
        );
    }

    private Dictionary<string, string> BuildHeaders()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = _node.Endpoint.Password,
            ["User-Id"] = _node.CurrentUserId.ToString(CultureInfo.InvariantCulture),
            ["Client-Name"] = ClientHeaders.UserAgent,
            ["User-Agent"] = ClientHeaders.UserAgent,
        };

        if (_node.ResumingEnabled && !string.IsNullOrEmpty(_node.SessionId))
            headers["Session-Id"] = _node.SessionId;

        return headers;
    }

    private void HandleTextMessage(int generation, int connectionEpoch, ReadOnlyMemory<byte> data)
    {
        if (_stopped || _latestGeneration != generation)
            return;

        try
        {
            _dispatcher.Dispatch(data.Span, connectionEpoch);
        }
        catch (Exception exception)
        {
            _node.HandleError(exception);
        }
    }

    private void HandleClosed(
        int generation,
        WebSocketCloseStatus? closeStatus,
        string? closeDescription,
        Exception? exception
    )
    {
        lock (_lock)
        {
            if (_stopped || _generation != generation)
                return;

            _lastCloseInfo = (closeStatus, closeDescription, exception);
            _disconnectSignal.TrySetResult();
        }
    }
}
