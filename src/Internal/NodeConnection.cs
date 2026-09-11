using System.Globalization;
using System.Net.WebSockets;
using Microsoft.Extensions.Logging;
using Nyra.WebSocket;

namespace Nyra.Internal;

/// <summary>
/// Owns the websocket lifecycle of a node: connects, feeds incoming messages to the dispatcher
/// and applies the reconnect policy (exponential backoff, configurable attempt limit). The
/// connect step runs a caller-supplied probe first, so it is retried together with the
/// websocket.
/// </summary>
internal sealed class NodeConnection : IDisposable
{
    private readonly Node _node;
    private readonly NodeOptions _options;
    private readonly NodeEventDispatcher _dispatcher;
    private readonly Func<CancellationToken, Task> _preConnect;
    private readonly Lock _lock = new();
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    private WebSocketClient? _client;
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

    public NodeConnection(
        Node node,
        NodeOptions options,
        NodeEventDispatcher dispatcher,
        Func<CancellationToken, Task> preConnect
    )
    {
        _node = node;
        _options = options;
        _dispatcher = dispatcher;
        _preConnect = preConnect;
    }

    public bool IsConnected
    {
        get
        {
            lock (_lock)
                return _client?.IsConnected ?? false;
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource lifetimeCts;
        lock (_lock)
        {
            if (_supervisionTask is not null)
                return;

            _stopped = false;
            _reconnectAttempts = 0;
            _everConnected = false;
            _disconnectSignal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            lifetimeCts = new CancellationTokenSource();
            _lifetimeCts = lifetimeCts;
            // Started before the first connect so a concurrent ConnectAsync cannot spawn a
            // second websocket and supervision loop.
            _supervisionTask = SupervisionLoopAsync(lifetimeCts.Token);
        }

        try
        {
            await ConnectOnceAsync(lifetimeCts.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Leave the supervision loop running: it retries the whole connect (probe included)
            // with the normal backoff, so a node that was down at startup recovers on its own.
            // ConnectAsync still throws so the caller knows the node is not usable yet. The
            // failure itself is logged by the supervision loop, not here, so it lands in order
            // with the "reconnecting" message that follows it instead of racing it.
            _initialConnectException = exception;
            _disconnectSignal.TrySetResult();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? supervisionTask;
        WebSocketClient? client;
        CancellationTokenSource? lifetimeCts;

        lock (_lock)
        {
            _stopped = true;
            _everConnected = false;
            supervisionTask = _supervisionTask;
            _supervisionTask = null;
            client = _client;
            _client = null;
            lifetimeCts = _lifetimeCts;
            _lifetimeCts = null;
        }

        if (client is not null)
            await StopClientAsync(client, cancellationToken).ConfigureAwait(false);

        TryCancel(lifetimeCts);
        _disconnectSignal.TrySetResult();

        if (supervisionTask is not null)
        {
            try
            {
                await supervisionTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The supervision loop never throws, this only guards against races.
            }
        }

        // A reconnect that raced this stop may have published a fresh client after the
        // snapshot above, stop that one too so nothing survives the stop.
        lock (_lock)
        {
            client = _client;
            _client = null;
        }

        if (client is not null)
            await StopClientAsync(client, cancellationToken).ConfigureAwait(false);

        if (lifetimeCts is not null)
        {
            try
            {
                lifetimeCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed by the supervision loop.
            }
        }
    }

    public void Dispose()
    {
        WebSocketClient? client;
        lock (_lock)
        {
            _stopped = true;
            TryCancel(_lifetimeCts);
            _lifetimeCts?.Dispose();
            _lifetimeCts = null;

            client = _client;
            _client = null;
            _supervisionTask = null;
        }

        // Disposing waits for the receive loop to unwind, doing it under the lock would stall IsConnected.
        client?.Dispose();

        _disconnectSignal.TrySetResult();
    }

    private async Task ConnectOnceAsync(CancellationToken lifetimeCt)
    {
        await _preConnect(lifetimeCt).ConfigureAwait(false);

        var client = new WebSocketClient();
        try
        {
            client.TextMessageReceived += OnTextMessage;
            client.Closed += OnClosed;
            await client
                .ConnectAsync(_node.Endpoint.ToWebSocketUri(), BuildHeaders(), lifetimeCt)
                .ConfigureAwait(false);
        }
        catch
        {
            client.TextMessageReceived -= OnTextMessage;
            client.Closed -= OnClosed;
            client.Dispose();
            throw;
        }

        WebSocketClient? previous;
        lock (_lock)
        {
            previous = _client;
            _client = client;
            _reconnectAttempts = 0;
            _everConnected = true;
        }

        // The replaced client is closed, that is why a new one was created, and its events
        // are unwired first so a still-live receive loop cannot re-signal a disconnect.
        if (previous is not null)
            await StopClientAsync(previous, CancellationToken.None).ConfigureAwait(false);

        _node.HandleConnected();
    }

    private async Task StopClientAsync(WebSocketClient client, CancellationToken cancellationToken)
    {
        client.TextMessageReceived -= OnTextMessage;
        client.Closed -= OnClosed;
        await client.StopAsync(cancellationToken).ConfigureAwait(false);
        client.Dispose();
    }

    private static void TryCancel(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by the supervision loop.
        }
    }

    private async Task SupervisionLoopAsync(CancellationToken lifetimeCt)
    {
        try
        {
            while (!lifetimeCt.IsCancellationRequested)
            {
                await _disconnectSignal.Task.WaitAsync(lifetimeCt).ConfigureAwait(false);
                lock (_lock)
                    _disconnectSignal = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );

                if (_stopped)
                    return;

                (var closeStatus, var closeDescription, var closeException) = _lastCloseInfo;
                if (_everConnected)
                {
                    _node.Logger?.LogWarning(
                        closeException,
                        "Node {NodeName} disconnected ({CloseStatus} {CloseDescription}), reconnecting.",
                        _node.Name,
                        closeStatus,
                        closeDescription
                    );
                    _node.HandleDisconnected(closeStatus, closeDescription, final: false);
                }
                // The initial connect failed before a websocket ever opened, there was no
                // connection to lose, so no Disconnected event, just the retry. Logged here
                // rather than by the caller of ConnectAsync so it lands in order with the
                // "reconnecting" message that follows instead of racing it.
                else
                {
                    ConnectionFailure.Log(
                        _node.Logger,
                        _node.Name,
                        _node.Endpoint,
                        initial: true,
                        _initialConnectException!
                    );
                }

                // Retry loop: a failed reconnect attempt must be followed by another attempt,
                // not by waiting for a new disconnect signal (a websocket that never connected
                // cannot produce one).
                while (!lifetimeCt.IsCancellationRequested && !_stopped)
                {
                    var attempt = Interlocked.Increment(ref _reconnectAttempts);
                    if (
                        _options.MaxReconnectAttempts >= 0
                        && attempt > _options.MaxReconnectAttempts
                    )
                    {
                        _node.Logger?.LogError(
                            "Node {NodeName} exceeded the maximum of {MaxAttempts} reconnect attempts, giving up.",
                            _node.Name,
                            _options.MaxReconnectAttempts
                        );
                        _node.HandleDisconnected(closeStatus, closeDescription, final: true);
                        EndSupervision();
                        return;
                    }

                    var delay = ComputeReconnectDelay(attempt);
                    _node.Logger?.LogInformation(
                        "Node {NodeName} reconnecting in {Delay} (attempt {Attempt}).",
                        _node.Name,
                        // Round for the log, the jittered delay below is what actually runs.
                        TimeSpan.FromSeconds(Math.Round(delay.TotalSeconds)),
                        _options.MaxReconnectAttempts < 0
                            ? attempt.ToString()
                            : $"{attempt}/{_options.MaxReconnectAttempts}"
                    );

                    try
                    {
                        await Task.Delay(delay, _timeProvider, lifetimeCt).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    try
                    {
                        await ConnectOnceAsync(lifetimeCt).ConfigureAwait(false);

                        // A stop that raced the connect may have missed the fresh client, take
                        // it over here so nothing survives the stop.
                        WebSocketClient? orphan;
                        lock (_lock)
                        {
                            orphan = _stopped ? _client : null;
                            if (orphan is not null)
                                _client = null;
                        }

                        if (orphan is not null)
                        {
                            await StopClientAsync(orphan, CancellationToken.None)
                                .ConfigureAwait(false);
                            return;
                        }

                        _node.Logger?.LogInformation("Node {NodeName} reconnected.", _node.Name);
                        break;
                    }
                    catch (OperationCanceledException) when (lifetimeCt.IsCancellationRequested)
                    {
                        // Stopping, not a failed attempt.
                        return;
                    }
                    catch (Exception exception)
                    {
                        ConnectionFailure.Log(
                            _node.Logger,
                            _node.Name,
                            _node.Endpoint,
                            initial: false,
                            exception
                        );
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping, nothing to do.
        }
        catch (Exception exception)
        {
            // Nothing in the loop is expected to throw past the inner handlers, but a dead
            // supervision loop would silently stop reconnections forever.
            EndSupervision();
            _node.HandleError(exception);
        }
    }

    /// <summary>Clears the supervision task so a later ConnectAsync can start the node again.</summary>
    private void EndSupervision()
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            cts = _lifetimeCts;
            _supervisionTask = null;
            _lifetimeCts = null;
        }

        cts?.Dispose();
    }

    private TimeSpan ComputeReconnectDelay(int attempt)
    {
        var delay = _options.ReconnectDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        delay = Math.Min(delay, _options.MaxReconnectDelay.TotalMilliseconds);
        // ±20% jitter so reconnecting clients do not retry in lockstep.
        return TimeSpan.FromMilliseconds(delay * (0.8 + Random.Shared.NextDouble() * 0.4));
    }

    private Dictionary<string, string> BuildHeaders()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = _node.Endpoint.Password,
            ["User-Id"] = _node.CurrentUserId.ToString(CultureInfo.InvariantCulture),
            ["Client-Name"] = ClientInfo.UserAgent,
            // NodeLink logs this to identify the client.
            ["User-Agent"] = ClientInfo.UserAgent,
        };

        if (_node.Options.Resuming && !string.IsNullOrEmpty(_node.SessionId))
            headers["Session-Id"] = _node.SessionId;

        return headers;
    }

    private void OnTextMessage(ReadOnlyMemory<byte> data)
    {
        try
        {
            _dispatcher.Dispatch(data.Span);
        }
        catch (Exception exception)
        {
            // A single poison message must never take down the receive loop.
            _node.HandleError(exception);
        }
    }

    private void OnClosed(
        WebSocketCloseStatus? closeStatus,
        string? closeDescription,
        Exception? exception
    )
    {
        _lastCloseInfo = (closeStatus, closeDescription, exception);
        _disconnectSignal.TrySetResult();
    }
}
