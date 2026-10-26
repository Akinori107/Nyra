using Microsoft.Extensions.Logging;
using Nyra.Entities;
using Nyra.EventArgs;

namespace Nyra.Internal;

internal sealed class NodeEventHandlers
{
    private readonly Node _node;

    private Timer? _readyWatchdog;

    private int _connectionEpoch;

    private int _readyAbortClaimedEpoch = -1;

    private bool _readyEpilogueRunning;

    private bool _resumingDisabledLogged;

    private (int Epoch, Stats Stats)? _statsBufferedDuringReady;

    private readonly ErrorThrottle _errors = new();

    internal NodeEventHandlers(Node node) => _node = node;

    private static string? UsableSessionId(string? sessionId) =>
        sessionId is null
        || sessionId.Length == 0
        || sessionId.Any(static c => c > 127 || char.IsControl(c) || char.IsWhiteSpace(c))
            ? null
            : sessionId;

    internal int HandleConnected(int generation)
    {
        int epoch;
        lock (_node.EventsLock)
        {
            if (_node.Stopped || _node.Disposed)
                return -1;

            if (!_node.Connection.IsCurrentGeneration(generation))
                return -1;

            epoch = ++_connectionEpoch;

            NyraMetrics.ReportConnection(_node, connected: true);

            _node.RaiseConnected(new NodeConnectedEventArgs(_node));
        }

        return epoch;
    }

    internal void ArmReadyWatchdog(int epoch, int generation)
    {
        lock (_node.EventsLock)
        {
            if (_node.Stopped || _node.Disposed)
                return;

            if (!_node.Connection.IsCurrentGeneration(generation))
                return;

            var timer = new Timer(
                static state =>
                {
                    var (events, token) = ((NodeEventHandlers, int))state!;
                    events.ReadyWatchdogFired(token);
                },
                (this, epoch),
                _node.Options.ReadyTimeout,
                Timeout.InfiniteTimeSpan
            );
            Interlocked.Exchange(ref _readyWatchdog, timer)?.Dispose();
        }
    }

    private void DisarmReadyWatchdog() => Interlocked.Exchange(ref _readyWatchdog, null)?.Dispose();

    private void ReadyWatchdogFired(int epoch)
    {
        TaskCompletionSource<bool>? completion;
        lock (_node.EventsLock)
        {
            if (epoch != _connectionEpoch || _node.Stopped || _node.Disposed || _node.IsReady)
                return;

            _readyAbortClaimedEpoch = epoch;

            DisarmReadyWatchdog();
            completion = _node.TakeConnectCompletion();
        }

        var exception = new TimeoutException(
            $"The server at {_node.Endpoint} did not send its ready operation within {_node.Options.ReadyTimeout.TotalSeconds:N0} seconds after the websocket connected."
        );
        completion?.TrySetException(exception);
        completion?.Task.ObserveFaults();

        try
        {
            _node.HandleError(exception);
        }
        catch { }

        _ = AbortAfterReadyTimeoutAsync(exception, epoch);
    }

    private async Task AbortAfterReadyTimeoutAsync(Exception reason, int epoch)
    {
        try
        {
            lock (_node.EventsLock)
                if (epoch != _connectionEpoch || _node.Stopped || _node.Disposed || _node.IsReady)
                    return;

            try
            {
                await _node
                    .Connection.AbortCurrentConnectionAsync("ready timeout", reason, epoch)
                    .ConfigureAwait(false);
            }
            finally
            {
                lock (_node.EventsLock)
                    if (_readyAbortClaimedEpoch == epoch)
                        _readyAbortClaimedEpoch = -1;
            }
        }
        catch (Exception exception)
        {
            _node.HandleError(exception);
        }
    }

    internal async Task HandleReadyAsync(NodeReadyEventArgs eventArgs, int connectionEpoch)
    {
        var presentedSessionId =
            _node.ResumingEnabled && !string.IsNullOrEmpty(_node.SessionId)
                ? _node.SessionId
                : null;

        string? usableSessionId;

        lock (_node.EventsLock)
        {
            if (_node.Stopped || _node.Disposed)
                return;

            if (connectionEpoch != _connectionEpoch)
                return;

            if (_readyAbortClaimedEpoch == _connectionEpoch)
                return;

            usableSessionId = UsableSessionId(eventArgs.SessionId);
            _node.SessionId = usableSessionId;
            _node.IsReady = true;
            _node.Connection.NoteReady();
            _readyEpilogueRunning = true;
        }

        var effectiveResumed =
            (eventArgs.Resumed ?? false)
            && presentedSessionId is not null
            && eventArgs.SessionId == presentedSessionId;

        if (!effectiveResumed && !_node.Connections.IsEmpty)
        {
            var invalidated = _node.Connections.Values.ToArray();

            foreach (var connection in invalidated)
                connection.MarkTearingDown();

            await InvalidateAsync(invalidated).ConfigureAwait(false);
        }

        if (effectiveResumed)
            foreach (var connection in _node.Connections.Values.ToArray())
                try
                {
                    await connection.ResendPendingVoiceAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _node.HandleError(exception);
                }

        try
        {
            lock (_node.EventsLock)
                if (_node.Stopped || connectionEpoch != _connectionEpoch)
                    return;

            _node.RaiseReady(
                new NodeReadyEventArgs
                {
                    Resumed = effectiveResumed,
                    ServerClaimedResume = eventArgs.Resumed,
                    SessionId = usableSessionId ?? string.Empty,
                }
            );
        }
        finally
        {
            Stats? bufferedStats;
            lock (_node.EventsLock)
            {
                if (connectionEpoch == _connectionEpoch)
                {
                    _readyEpilogueRunning = false;
                    bufferedStats =
                        _statsBufferedDuringReady is { } buffered
                        && buffered.Epoch == connectionEpoch
                            ? buffered.Stats
                            : null;
                    _statsBufferedDuringReady = null;
                }
                else
                    bufferedStats = null;

                if (bufferedStats is { } lateStats && !_node.Stopped && !_node.Disposed)
                    _node.RaiseStatsUpdated(new NodeStatsEventArgs(lateStats));
            }
        }

        lock (_node.EventsLock)
        {
            if (_node.Stopped || connectionEpoch != _connectionEpoch)
                return;

            DisarmReadyWatchdog();
            _node.TakeConnectCompletion()?.TrySetResult(true);
        }

        if (effectiveResumed)
            _ = _node.GuardAsync(_node.Registry.DestroyOrphanPlayersAsync(CancellationToken.None));

        if (!_node.ResumingEnabled || effectiveResumed)
            return;

        if (_node.SessionId is null)
        {
            if (!_resumingDisabledLogged)
            {
                _resumingDisabledLogged = true;
                try
                {
                    _node.Logger?.LogWarning(
                        LogEvents.Node,
                        "The session id of node {NodeName} was dropped as header-hostile, session resuming stays disabled until the server sends a usable one.",
                        _node.Name
                    );
                }
                catch { }
            }
            return;
        }

        _resumingDisabledLogged = false;

        try
        {
            await _node
                .Rest.Session.UpdateAsync(true, _node.ResumeTimeoutSeconds, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _node.HandleError(
                new InvalidOperationException(
                    $"Failed to enable session resuming on node '{_node.Name}': {exception.Message}",
                    exception
                )
            );
        }
    }

    private Task InvalidateAsync(GuildConnection[] invalidated) =>
        Task.WhenAll(
            Array.ConvertAll(
                invalidated,
                connection => _node.GuardAsync(connection.Teardown.HandleSessionInvalidatedAsync())
            )
        );

    internal void HandleStats(Stats stats)
    {
        lock (_node.EventsLock)
        {
            if (_node.Stopped || _node.Disposed)
                return;

            _node.Stats = stats;
            NyraMetrics.ReportStats(_node, stats);

            if (_readyEpilogueRunning)
            {
                _statsBufferedDuringReady = (_connectionEpoch, stats);
                return;
            }
        }

        _node.RaiseStatsUpdated(new NodeStatsEventArgs(stats));
    }

    internal GuildConnection[] SnapshotWorkerFailed(WorkerFailedEventArgs eventArgs)
    {
        if (_node.Stopped || _node.Disposed)
            return [];

        if (eventArgs.AffectedGuilds is not { Count: > 0 } guilds)
            return [];

        var guildSet = guilds.ToHashSet();
        var invalidated = _node
            .Connections.Values.Where(connection =>
                guildSet.Contains(connection.GuildId) && connection.IsPlayerCommitted
            )
            .ToArray();

        foreach (var connection in invalidated)
            connection.MarkTearingDown();

        return invalidated;
    }

    internal async Task HandleWorkerFailedAsync(
        GuildConnection[] invalidated,
        WorkerFailedEventArgs eventArgs
    )
    {
        if (_node.Stopped || _node.Disposed)
            return;

        if (eventArgs.AffectedGuilds is { Count: > 0 })
            await InvalidateAsync(invalidated).ConfigureAwait(false);

        _node.RaiseWorkerFailed(eventArgs);
    }

    internal void HandleConnectionStatus(ConnectionStatus status)
    {
        lock (_node.EventsLock)
        {
            if (_node.Stopped || _node.Disposed)
                return;
            _node.ConnectionStatus = status;
            _node.RaiseConnectionStatusUpdated(new NodeConnectionStatusEventArgs(status));
        }
    }

    internal void HandleDisconnected(
        System.Net.WebSockets.WebSocketCloseStatus? closeStatus,
        string? closeDescription,
        Exception? exception,
        bool final,
        int ownerGeneration
    )
    {
        TaskCompletionSource<bool>? completion;
        lock (_node.EventsLock)
        {
            if (_node.Connection.LatestGeneration != ownerGeneration)
                return;

            if (!_node.Disposed)
                NyraMetrics.ReportConnection(_node, connected: false);

            if (_node.Stopped)
                return;

            _connectionEpoch++;

            _readyEpilogueRunning = false;

            completion = _node.TakeConnectCompletion(ownerGeneration);

            _node.IsReady = false;

            DisarmReadyWatchdog();
            _node.InvalidateServerInfo();

            completion?.TrySetResult(false);

            _node.RaiseDisconnected(
                new NodeDisconnectedEventArgs(closeStatus, closeDescription, final, exception)
            );
        }
    }

    internal void HandleError(Exception exception)
    {
        if (!_errors.TryAdmit(exception, out var suppressed))
            return;

        if (suppressed > 0)
            try
            {
                _node.Logger?.LogWarning(
                    LogEvents.Node,
                    "{Count} more errors of the same kind on node {NodeName} were suppressed in the last {Window} seconds.",
                    suppressed,
                    _node.Name,
                    ErrorThrottle.WindowMilliseconds / 1000
                );
            }
            catch { }

        try
        {
            _node.Logger?.LogError(
                LogEvents.Node,
                exception,
                "An error occurred on node {NodeName}.",
                _node.Name
            );
        }
        catch { }

        _node.RaiseError(new NodeErrorEventArgs(exception, _node));
    }

    internal void OnStopped()
    {
        lock (_node.EventsLock)
        {
            _connectionEpoch++;
            DisarmReadyWatchdog();
        }
    }
}
