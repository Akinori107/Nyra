using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Nyra.Abstractions;
using Nyra.Exceptions;

namespace Nyra.Internal;

internal sealed class VoiceHandshakeCoordinator
{
    private readonly ConcurrentDictionary<ulong, PendingHandshake> _pending = new();

    private static readonly ConditionalWeakTable<
        IDiscordBridge,
        ConcurrentDictionary<ulong, LeaveEchoGuard>
    > LeaveEchoGuards = new();

    private static ConcurrentDictionary<ulong, LeaveEchoGuard> GuardFor(IDiscordBridge client) =>
        LeaveEchoGuards.GetValue(
            client,
            static _ => new ConcurrentDictionary<ulong, LeaveEchoGuard>()
        );

    private static LeaveEchoGuard GuardForGuild(
        ConcurrentDictionary<ulong, LeaveEchoGuard> guard,
        ulong guildId
    ) => guard.GetOrAdd(guildId, static _ => new LeaveEchoGuard());

    private static long ArmGuard(IDiscordBridge client, ulong guildId, long deadline)
    {
        var guards = GuardFor(client);
        var armed = GuardForGuild(guards, guildId);
        var ticket = armed.Arm(deadline);
        var canonical = guards.GetOrAdd(guildId, armed);
        if (ReferenceEquals(canonical, armed))
            return ticket;
        var reTicket = canonical.Arm(deadline);
        armed.Disarm(ticket);
        return reTicket;
    }

    private static void EvictIfIdle(
        ConcurrentDictionary<ulong, LeaveEchoGuard> guard,
        ulong guildId,
        LeaveEchoGuard guildFence,
        ConcurrentDictionary<ulong, PendingHandshake> pending
    )
    {
        if (pending.TryGetValue(guildId, out _))
            return;
        guildFence.EvictIfIdleUnderLock(() =>
        {
            var pairs = (ICollection<KeyValuePair<ulong, LeaveEchoGuard>>)guard;
            pairs.Remove(new KeyValuePair<ulong, LeaveEchoGuard>(guildId, guildFence));
        });
    }

    private sealed class LeaveEchoGuard
    {
        private const int MaxOwedEchoes = 16;

        private readonly Lock _lock = new();

        private readonly Queue<OwedEcho> _owedEchoes = new();
        private long _ticketSequence;

        private readonly record struct OwedEcho(long Deadline, long Ticket);

        public void EvictIfIdleUnderLock(Action evict)
        {
            lock (_lock)
            {
                var now = Environment.TickCount64;
                while (_owedEchoes.Count > 0 && _owedEchoes.Peek().Deadline <= now)
                    _owedEchoes.Dequeue();

                if (_owedEchoes.Count != 0)
                    return;
                evict();
            }
        }

        public long Arm(long deadline)
        {
            lock (_lock)
            {
                while (_owedEchoes.Count >= MaxOwedEchoes)
                    _owedEchoes.Dequeue();

                var ticket = ++_ticketSequence;
                _owedEchoes.Enqueue(new OwedEcho(deadline, ticket));
                return ticket;
            }
        }

        public void Disarm(long ticket)
        {
            lock (_lock)
            {
                var kept = new List<OwedEcho>(_owedEchoes.Count);
                var removed = false;
                foreach (var owed in _owedEchoes)
                    if (!removed && owed.Ticket == ticket)
                        removed = true;
                    else
                        kept.Add(owed);

                if (!removed)
                    return;

                _owedEchoes.Clear();
                foreach (var owed in kept)
                    _owedEchoes.Enqueue(owed);
            }
        }

        public bool IsSkipping(long now)
        {
            lock (_lock)
            {
                foreach (var owed in _owedEchoes)
                    if (owed.Deadline > now)
                        return true;

                return false;
            }
        }

        public bool TryConsumeEcho(long now)
        {
            lock (_lock)
            {
                while (_owedEchoes.Count > 0 && _owedEchoes.Peek().Deadline <= now)
                    _owedEchoes.Dequeue();

                if (_owedEchoes.Count == 0)
                    return false;

                _owedEchoes.Dequeue();
                return true;
            }
        }
    }

    private static readonly ConditionalWeakTable<
        VoiceStateUpdatedEventArgs,
        object
    > ConsumedEchoes = new();

    private static string DescribeMovedChannel(ulong? channelId) =>
        channelId is { } id ? $" (channel {id})" : string.Empty;

    private static readonly object Marker = new();

    private readonly TimeSpan _timeout;

    public VoiceHandshakeCoordinator(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    private string DescribeTimeout()
    {
        if (_timeout < TimeSpan.FromSeconds(1))
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0} milliseconds",
                _timeout.TotalMilliseconds
            );

        var seconds = Math.Round(_timeout.TotalSeconds, MidpointRounding.AwayFromZero);
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:N0} second{1}",
            seconds,
            seconds == 1 ? string.Empty : "s"
        );
    }

    private static bool IsSkippingStaleEvents(
        ConcurrentDictionary<ulong, LeaveEchoGuard> guard,
        ulong guildId
    ) =>
        guard.TryGetValue(guildId, out var guildFence)
        && guildFence.IsSkipping(Environment.TickCount64);

    public bool HandleVoiceStateUpdate(IDiscordBridge client, VoiceStateUpdatedEventArgs eventArgs)
    {
        var guard = GuardFor(client);

        if (guard.TryGetValue(eventArgs.GuildId, out var guildFence))
        {
            EvictIfIdle(guard, eventArgs.GuildId, guildFence, _pending);

            if (eventArgs.VoiceState.ChannelId is null)
            {
                if (
                    !ConsumedEchoes.TryGetValue(eventArgs, out _)
                    && guildFence.TryConsumeEcho(Environment.TickCount64)
                )
                {
                    ConsumedEchoes.GetValue(eventArgs, static _ => Marker);
                    EvictIfIdle(guard, eventArgs.GuildId, guildFence, _pending);
                    return true;
                }

                if (guildFence.IsSkipping(Environment.TickCount64))
                    return true;
            }
            else if (guildFence.IsSkipping(Environment.TickCount64))
                return true;
        }

        if (!_pending.TryGetValue(eventArgs.GuildId, out var pending))
            return false;

        if (eventArgs.VoiceState.ChannelId == pending.ChannelId)
        {
            if (
                pending.State.Task.IsCompletedSuccessfully
                && pending.State.Task.Result.SessionId != eventArgs.VoiceState.SessionId
            )
            {
                pending.Moved.TrySetResult(eventArgs.VoiceState.ChannelId);
                return true;
            }

            pending.Removed = false;
            pending.State.TrySetResult(eventArgs.VoiceState);
            return true;
        }

        if (eventArgs.VoiceState.ChannelId is null)
        {
            pending.Removed = true;

            if (pending.State.Task.IsCompletedSuccessfully)
            {
                if (pending.Server.Task.IsCompletedSuccessfully)
                    pending.RemovedAfterOutcome = true;
                else
                {
                    pending.KickedAfterState = true;
                    pending.Moved.TrySetResult(null);
                }
            }
            return true;
        }

        if (
            pending.State.Task.IsCompletedSuccessfully
            && pending.Server.Task.IsCompletedSuccessfully
        )
            pending.RemovedAfterOutcome = true;
        pending.Moved.TrySetResult(eventArgs.VoiceState.ChannelId);
        return true;
    }

    public bool HandleVoiceServerUpdate(
        IDiscordBridge client,
        VoiceServerUpdatedEventArgs eventArgs
    )
    {
        if (
            string.IsNullOrWhiteSpace(eventArgs.VoiceServer.Endpoint)
            || string.IsNullOrWhiteSpace(eventArgs.VoiceServer.Token)
        )
            return false;

        if (IsSkippingStaleEvents(GuardFor(client), eventArgs.GuildId))
            if (
                !_pending.TryGetValue(eventArgs.GuildId, out var echoingPending)
                || !echoingPending.State.Task.IsCompletedSuccessfully
            )
                return true;

        if (_pending.TryGetValue(eventArgs.GuildId, out var pending))
        {
            if (pending.Moved.Task.IsCompleted || pending.Removed)
                return false;

            pending.Server.TrySetResult(eventArgs.VoiceServer);
            return true;
        }

        return false;
    }

    internal void FailPendingGuildRemoval(ulong guildId)
    {
        _removalFence.Mark(guildId);
        if (_pending.TryGetValue(guildId, out var pending))
        {
            pending.GuildRemoved = true;
            pending.Moved.TrySetResult(null);
        }
    }

    private readonly GuildRemovalFence _removalFence = new();

    internal bool RemovedSince(ulong guildId, long sinceTick)
    {
        var removed = _removalFence.RemovedSince(guildId, sinceTick);
        _removalFence.Forget(guildId);
        return removed;
    }

    private PendingHandshake RegisterPending(ulong guildId, ulong channelId)
    {
        var pending = new PendingHandshake(channelId);
        if (!_pending.TryAdd(guildId, pending))
            throw new InvalidOperationException(
                $"A voice handshake is already in progress for guild {guildId}."
            );
        return pending;
    }

    public async Task<(DiscordVoiceState State, VoiceServer Server)> WaitForUpdatesAsync(
        ulong guildId,
        ulong channelId,
        IDiscordBridge client,
        CancellationToken cancellationToken
    )
    {
        var pending = RegisterPending(guildId, channelId);

        Task? joinSend = null;
        Task<DiscordVoiceState>? stateTask = null;

        using var joinCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        joinCts.CancelAfter(_timeout);
        try
        {
            try
            {
                joinSend = SendVoiceUpdateSafely(client, guildId, channelId, joinCts.Token);
                try
                {
                    await joinSend.WaitAsync(joinCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    if (!joinSend.IsCompleted || joinSend.IsFaulted)
                        joinSend.ObserveFaults();
                }
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw joinCts.IsCancellationRequested
                    ? new VoiceHandshakeException(
                        $"The voice state update for guild {guildId} timed out after {DescribeTimeout()}: "
                            + "the join's voice state update send did not complete within the bound, the Discord gateway may be stuck reconnecting.",
                        exception
                    )
                    : new VoiceHandshakeException(
                        $"The Discord bridge could not send the voice state update for guild {guildId}, the gateway is probably closed or reconnecting, connect again once it is back: {exception.InnerException?.Message ?? exception.Message}",
                        exception
                    );
            }

            stateTask = pending.State.Task;
            var serverTask = pending.Server.Task;
            var movedTask = pending.Moved.Task;

            try
            {
                var updatesTask = Task.WhenAll(stateTask, serverTask);
                var completed = await Task.WhenAny(updatesTask, movedTask)
                    .WaitAsync(_timeout, cancellationToken)
                    .ConfigureAwait(false);

                if (ReferenceEquals(completed, movedTask))
                {
                    if (pending.GuildRemoved)
                        throw new VoiceHandshakeException(
                            $"The bot was removed from the guild during the handshake for guild {guildId}. "
                                + "The credentials of the join are stale, re-invite the bot and connect again."
                        );

                    var tail = pending.KickedAfterState
                        ? "connect again when the bot is back in a voice channel."
                        : "the rollback of the failed join also leaves the channel the bot ended up in"
                            + DescribeMovedChannel(movedTask.Result)
                            + ", connect again to that channel.";
                    throw new VoiceHandshakeException(
                        $"The bot was moved or removed from the voice channel during the handshake for guild {guildId}. "
                            + "The credentials of the join are stale, "
                            + tail
                    );
                }

                await updatesTask.ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                if (pending.Removed)
                    throw new VoiceHandshakeException(
                        $"The bot was moved or removed from the voice channel during the handshake for guild {guildId}. "
                            + "The credentials of the join are stale, the rollback of the failed join also leaves any channel the bot ended up in"
                            + DescribeMovedChannel(
                                pending.Moved.Task.IsCompletedSuccessfully
                                    ? pending.Moved.Task.Result
                                    : null
                            )
                            + ", connect again when the bot is back in a voice channel.",
                        exception
                    );

                throw new VoiceHandshakeException(
                    $"The voice handshake for guild {guildId} timed out after {DescribeTimeout()}. "
                        + (
                            stateTask.IsCompletedSuccessfully && !serverTask.IsCompleted
                                ? "Discord confirmed the voice state but sent no voice server. This usually means the bot was already in that channel on Discord's side, for example after a leave that never reached Discord, or that Discord had no voice server for the region. The rollback of this attempt sent a leave, so connecting again now should work."
                                : "Make sure the bot has permission to join the channel and that the guild voice states intent is enabled. A connect right after disconnecting a bot that was already out of voice can also time out, retry once the timeout has passed."
                        ),
                    exception
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (VoiceHandshakeException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new VoiceHandshakeException(
                    $"The voice handshake for guild {guildId} failed: {exception.Message}",
                    exception
                );
            }

            if (pending.RemovedAfterOutcome)
                throw new VoiceHandshakeException(
                    $"The bot was moved or removed from the voice channel right after the handshake for guild {guildId} completed. "
                        + "The credentials of the join are stale, connect again when the bot is back in a voice channel."
                );

            return (stateTask.Result, serverTask.Result);
        }
        catch
        {
            if (joinSend is not { Status: TaskStatus.Faulted })
            {
                long? armTicket =
                    stateTask?.IsCompletedSuccessfully == true
                    && !pending.Removed
                    && !pending.GuildRemoved
                        ? ArmGuard(
                            client,
                            guildId,
                            Environment.TickCount64 + (long)_timeout.TotalMilliseconds
                        )
                        : null;

                var compensationCts = new CancellationTokenSource(_timeout);
                var leaveSend = SendVoiceUpdateSafely(client, guildId, null, compensationCts.Token);

                _ = ObserveCompensationAsync(
                    leaveSend,
                    GuardForGuild(GuardFor(client), guildId),
                    armTicket,
                    compensationCts
                );
            }

            throw;
        }
        finally
        {
            _pending.TryRemove(guildId, out _);
        }
    }

    public async Task LeaveChannelAsync(IDiscordBridge client, ulong guildId)
    {
        var deadline = Environment.TickCount64 + (long)_timeout.TotalMilliseconds;
        var armTicket = ArmGuard(client, guildId, deadline);

        try
        {
            using var leaveCts = new CancellationTokenSource(_timeout);
            var leaveSend = SendVoiceUpdateSafely(client, guildId, null, leaveCts.Token);
            try
            {
                await leaveSend.WaitAsync(leaveCts.Token).ConfigureAwait(false);
            }
            finally
            {
                if (!leaveSend.IsCompleted || leaveSend.IsFaulted)
                    leaveSend.ObserveFaults();

                if (leaveSend.Status == TaskStatus.Faulted)
                    GuardForGuild(GuardFor(client), guildId).Disarm(armTicket);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static Task SendVoiceUpdateSafely(
        IDiscordBridge client,
        ulong guildId,
        ulong? channelId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return client.SendVoiceUpdateAsync(
                guildId,
                channelId,
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }

    private static async Task ObserveCompensationAsync(
        Task send,
        LeaveEchoGuard guard,
        long? armTicket,
        CancellationTokenSource compensationCts
    )
    {
        try
        {
            await send.WaitAsync(compensationCts.Token).ConfigureAwait(false);
        }
        catch { }
        finally
        {
            if (!send.IsCompleted || send.IsFaulted)
                send.ObserveFaults();

            if (send.Status == TaskStatus.Faulted && armTicket is { } ticket)
                guard.Disarm(ticket);
            compensationCts.Dispose();
        }
    }

    private sealed class PendingHandshake(ulong channelId)
    {
        public ulong ChannelId { get; } = channelId;

        public TaskCompletionSource<DiscordVoiceState> State { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<VoiceServer> Server { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ulong?> Moved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public volatile bool Removed;

        public volatile bool GuildRemoved;

        public volatile bool KickedAfterState;

        public volatile bool RemovedAfterOutcome;
    }
}
