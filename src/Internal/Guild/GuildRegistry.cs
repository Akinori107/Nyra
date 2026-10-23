using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra.Internal;

internal sealed class GuildRegistry
{
    private const int MaxConcurrentDestroys = 8;

    private readonly Node _node;

    private readonly SemaphoreSlim _destroyLimiter = new(MaxConcurrentDestroys);

    internal GuildRegistry(Node node) => _node = node;

    internal ConcurrentDictionary<ulong, GuildConnection> Connections { get; } = new();

    internal async Task<GuildConnection> ConnectAsync(
        ulong guildId,
        ulong channelId,
        CancellationToken cancellationToken
    )
    {
        _node.ThrowIfDisposed();

        if (_node.Client is null)
            throw new InvalidOperationException(
                "The node is not attached to a Discord client yet, start it through the extension first."
            );

        if (!_node.IsReady)
            throw new InvalidOperationException($"The node '{_node.Name}' is not ready yet.");

        using var lease = await EnterGateAsync(guildId, cancellationToken).ConfigureAwait(false);

        var joinStartTick = Environment.TickCount64;

        NyraExtension? claimed = null;
        var tookOverConnection = false;
        try
        {
            if (Connections.TryGetValue(guildId, out var existing))
            {
                if (existing.IsTearingDown || existing.ChannelId is null)
                {
                    if (_node.Extension is { } takeoverExtension)
                    {
                        if (!takeoverExtension.TryClaimGuild(guildId, _node, out var owner))
                            throw new InvalidOperationException(
                                $"The bot is already connected in this guild through node '{owner!.Name}'. Disconnect it before connecting through '{_node.Name}'."
                            );
                        claimed = takeoverExtension;
                    }

                    Connections.TryRemove(
                        new KeyValuePair<ulong, GuildConnection>(guildId, existing)
                    );
                    try
                    {
                        var voiceAbort = await CancellationOf(
                                existing.DrainVoiceForwardAsync(cancellationToken)
                            )
                            .ConfigureAwait(false);
                        var filtersAbort = await CancellationOf(
                                existing.DrainFiltersAsync(cancellationToken)
                            )
                            .ConfigureAwait(false);
                        var patchesAbort = await CancellationOf(
                                existing.Patches.DrainAsync(cancellationToken)
                            )
                            .ConfigureAwait(false);

                        if ((voiceAbort ?? filtersAbort ?? patchesAbort) is { } cancelledConnect)
                            throw cancelledConnect;
                    }
                    finally
                    {
                        await existing.CompleteEventPumpAsync().ConfigureAwait(false);
                    }

                    existing.ChannelId = null;
                    tookOverConnection = true;
                }
                else if (existing.ChannelId == channelId)
                    return existing;
                else
                    throw new InvalidOperationException(
                        $"The bot is already connected to channel {existing.ChannelId} in this guild. Disconnect it before connecting to channel {channelId}."
                    );
            }

            if (_node.Extension is { } extension)
            {
                if (claimed is not null && !ReferenceEquals(claimed, extension))
                    claimed.ReleaseGuildClaim(guildId, _node);
                if (!extension.TryClaimGuild(guildId, _node, out var owner))
                {
                    if (tookOverConnection)
                        await _node
                            .VoiceHandshake.LeaveChannelAsync(_node.Client, guildId)
                            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                    throw new InvalidOperationException(
                        $"The bot is already connected in this guild through node '{owner!.Name}'. Disconnect it before connecting through '{_node.Name}'."
                    );
                }
                claimed = extension;
            }

            var (state, server) = await _node
                .VoiceHandshake.WaitForUpdatesAsync(
                    guildId,
                    channelId,
                    _node.Client,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(state.SessionId))
            {
                await _node
                    .VoiceHandshake.LeaveChannelAsync(_node.Client, guildId)
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                throw new VoiceHandshakeException(
                    $"The gateway's voice state for guild {guildId} carried no usable session id, the join cannot proceed."
                );
            }

            var sessionId = state.SessionId;

            var connection = new GuildConnection(
                _node,
                guildId,
                channelId,
                sessionId,
                server.Endpoint,
                server.Token
            );

            if (!_node.IsReady)
            {
                await _node
                    .VoiceHandshake.LeaveChannelAsync(_node.Client, guildId)
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

                _ = connection.CompleteEventPumpAsync();

                throw new InvalidOperationException($"The node '{_node.Name}' is not ready yet.");
            }

            Connections[guildId] = connection;
            try
            {
                using var patchCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                patchCts.CancelAfter(_node.Options.VoiceHandshakeTimeout);
                await _node
                    .Rest.Players.UpdateAsync(
                        guildId,
                        new PlayerUpdatePayload
                        {
                            Voice = new PlayerVoiceState
                            {
                                Token = server.Token,
                                Endpoint = server.Endpoint,
                                SessionId = state.SessionId,
                                ChannelId = channelId,
                            },
                        },
                        patchCts.Token
                    )
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await RollbackRegistrationAsync(
                        guildId,
                        connection,
                        sendLeave: true,
                        completePump: true
                    )
                    .ConfigureAwait(false);
                throw new VoiceHandshakeException(
                    $"The player registration for guild {guildId} did not answer within the voice handshake timeout, the node may be overloaded or unable to reach the Discord voice server (Lavalink answers this request only once its voice connection is up)."
                );
            }
            catch
            {
                await RollbackRegistrationAsync(
                        guildId,
                        connection,
                        sendLeave: true,
                        completePump: true
                    )
                    .ConfigureAwait(false);
                throw;
            }

            if (connection.IsTearingDown)
            {
                await RollbackRegistrationAsync(
                        guildId,
                        connection,
                        sendLeave: true,
                        completePump: false
                    )
                    .ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"The connection of guild {guildId} was invalidated while connecting, connect again."
                );
            }

            if (_node.VoiceHandshake.RemovedSince(guildId, joinStartTick))
            {
                await RollbackRegistrationAsync(
                        guildId,
                        connection,
                        sendLeave: false,
                        completePump: true
                    )
                    .ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"The bot was removed from guild {guildId} while connecting, connect again when it is back in the guild."
                );
            }

            connection.CommitPlayerUpdate();

            return connection;
        }
        finally
        {
            claimed?.ReleaseGuildClaim(guildId, _node);
        }
    }

    private static async Task<OperationCanceledException?> CancellationOf(Task drain)
    {
        try
        {
            await drain.ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException cancelled)
        {
            return cancelled;
        }
    }

    internal GuildConnection? GetConnection(ulong guildId) =>
        Connections.TryGetValue(guildId, out var connection) ? connection : null;

    private async Task RollbackRegistrationAsync(
        ulong guildId,
        GuildConnection connection,
        bool sendLeave,
        bool completePump
    )
    {
        Connections.TryRemove(new KeyValuePair<ulong, GuildConnection>(guildId, connection));
        try
        {
            await _node
                .Rest.Players.DestroyAsync(guildId, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception) { }
        if (sendLeave && _node.Client is { } client)
            await _node
                .VoiceHandshake.LeaveChannelAsync(client, guildId)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (completePump)
            _ = connection.CompleteEventPumpAsync();
    }

    private readonly Dictionary<ulong, (SemaphoreSlim Semaphore, int RefCount)> _guildGates = new();
    private readonly Lock _guildGatesLock = new();

    private SemaphoreSlim AcquireGate(ulong guildId)
    {
        lock (_guildGatesLock)
        {
            if (_guildGates.TryGetValue(guildId, out var entry))
            {
                _guildGates[guildId] = (entry.Semaphore, entry.RefCount + 1);
                return entry.Semaphore;
            }

            var semaphore = new SemaphoreSlim(1, 1);
            _guildGates[guildId] = (semaphore, 1);
            return semaphore;
        }
    }

    private async Task<GateLease> EnterGateAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var gate = AcquireGate(guildId);
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleaseGate(guildId);
            throw;
        }
        return new GateLease(this, guildId, gate);
    }

    private readonly struct GateLease(GuildRegistry registry, ulong guildId, SemaphoreSlim gate)
        : IDisposable
    {
        public void Dispose()
        {
            gate.Release();
            registry.ReleaseGate(guildId);
        }
    }

    private void ReleaseGate(ulong guildId)
    {
        lock (_guildGatesLock)
        {
            if (!_guildGates.TryGetValue(guildId, out var entry))
                return;

            if (entry.RefCount <= 1)
                _guildGates.Remove(guildId);
            else
                _guildGates[guildId] = (entry.Semaphore, entry.RefCount - 1);
        }
    }

    internal async Task<bool> DestroyPlayerExclusivelyAsync(
        GuildConnection connection,
        CancellationToken cancellationToken
    )
    {
        using var lease = await EnterGateAsync(connection.GuildId, cancellationToken)
            .ConfigureAwait(false);

        return await DestroyPlayerUnderGateAsync(connection, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task DestroyOrphanPlayersAsync(CancellationToken cancellationToken)
    {
        var players = await _node.Rest.Players.ListAsync(cancellationToken).ConfigureAwait(false);
        var destroyed = 0;
        foreach (var player in players.Where(player => !Connections.ContainsKey(player.GuildId)))
        {
            using var lease = await EnterGateAsync(player.GuildId, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                if (Connections.ContainsKey(player.GuildId))
                    continue;

                await _node
                    .Rest.Players.DestroyAsync(player.GuildId, cancellationToken)
                    .ConfigureAwait(false);
                destroyed++;
            }
            catch (RestException exception) when (exception.StatusCode == 404) { }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _node.HandleError(exception);
            }
        }

        if (destroyed > 0)
            try
            {
                _node.Logger?.LogInformation(
                    LogEvents.Node,
                    "Node {NodeName} resumed its session with {Count} player(s) no guild connection owns, they were destroyed.",
                    _node.Name,
                    destroyed
                );
            }
            catch { }
    }

    internal async Task<bool> DisconnectExclusivelyAsync(
        GuildConnection connection,
        CancellationToken cancellationToken
    )
    {
        using var lease = await EnterGateAsync(connection.GuildId, cancellationToken)
            .ConfigureAwait(false);

        if (
            !IsTakenOver(connection)
            && connection.ChannelId is not null
            && connection.TryClaimVoiceLeave()
        )
            try
            {
                await SendVoiceLeaveAsync(connection.GuildId).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                connection.ReleaseVoiceLeave();
                _node.HandleError(exception);
            }

        return await DestroyPlayerUnderGateAsync(connection, cancellationToken)
            .ConfigureAwait(false);
    }

    private bool IsTakenOver(GuildConnection connection) =>
        Connections.TryGetValue(connection.GuildId, out var current)
        && !ReferenceEquals(current, connection);

    private async Task<bool> DestroyPlayerUnderGateAsync(
        GuildConnection connection,
        CancellationToken cancellationToken
    )
    {
        if (IsTakenOver(connection))
            return false;

        Connections.TryRemove(
            new KeyValuePair<ulong, GuildConnection>(connection.GuildId, connection)
        );

        try
        {
            await _destroyLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _node
                    .Rest.Players.DestroyAsync(connection.GuildId, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _destroyLimiter.Release();
            }

            return true;
        }
        catch (RestException exception) when (exception.StatusCode == 404)
        {
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    internal async Task DisconnectGuildsAsync(CancellationToken cancellationToken = default)
    {
        var stalledPasses = 0;
        while (!Connections.IsEmpty)
        {
            var remaining = Connections.Count;
            var batch = Connections.Values.ToArray();
            using var limiter = new SemaphoreSlim(8);
            await Task.WhenAll(
                    batch.Select(async connection =>
                    {
                        var acquired = false;
                        try
                        {
                            await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);
                            acquired = true;
                            await connection
                                .DisconnectAsync(cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                            when (cancellationToken.IsCancellationRequested) { }
                        catch (Exception exception)
                        {
                            _node.HandleError(exception);
                        }
                        finally
                        {
                            if (acquired)
                                limiter.Release();
                        }
                    })
                )
                .ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                return;

            if (Connections.Count >= remaining)
            {
                if (++stalledPasses >= 2)
                    break;
            }
            else
                stalledPasses = 0;
        }
    }

    internal void DisconnectGuilds()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            DisconnectGuildsAsync(deadline.Token).Wait(TimeSpan.FromSeconds(15));
        }
        catch (Exception) { }
    }

    internal Task SendVoiceLeaveAsync(ulong guildId) =>
        _node.VoiceHandshake.LeaveChannelAsync(
            _node.Client
                ?? throw new InvalidOperationException(
                    $"The node has no Discord client to send the voice leave of guild {guildId} through."
                ),
            guildId
        );
}
