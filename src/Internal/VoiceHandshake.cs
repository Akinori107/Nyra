using System.Collections.Concurrent;
using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.Net.Abstractions;
using Nyra.Exceptions;

namespace Nyra.Internal;

/// <summary>
/// Coordinates Discord voice handshakes: sends the gateway voice state update and awaits the
/// matching voice state and voice server updates, with a timeout. One handshake per guild at
/// a time.
/// </summary>
internal sealed class VoiceHandshakeCoordinator
{
    private readonly ConcurrentDictionary<ulong, PendingHandshake> _pending = new();
    private readonly TimeSpan _timeout;

    public VoiceHandshakeCoordinator(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    public void HandleVoiceStateUpdate(DiscordClient client, VoiceStateUpdateEventArgs eventArgs)
    {
        if (eventArgs.Guild is null || eventArgs.User?.Id != client.CurrentUser.Id)
            return;

        if (_pending.TryGetValue(eventArgs.Guild.Id, out var pending))
            pending.State.TrySetResult(eventArgs);
    }

    /// <summary>Feeds a voice server update to a pending handshake, false when none was waiting for it.</summary>
    public bool HandleVoiceServerUpdate(VoiceServerUpdateEventArgs eventArgs)
    {
        if (eventArgs.Guild is null)
            return false;

        // Discord sometimes sends a partial voice server update with an empty endpoint, both
        // audio servers reject it (400), so wait for the complete update instead of failing.
        if (string.IsNullOrWhiteSpace(eventArgs.Endpoint))
            return false;

        if (_pending.TryGetValue(eventArgs.Guild.Id, out var pending))
        {
            pending.Server.TrySetResult(eventArgs);
            return true;
        }

        return false;
    }

    public async Task<(
        VoiceStateUpdateEventArgs State,
        VoiceServerUpdateEventArgs Server
    )> WaitForUpdatesAsync(
        ulong guildId,
        ulong channelId,
        DiscordClient discord,
        CancellationToken cancellationToken
    )
    {
        var pending = new PendingHandshake();
        if (!_pending.TryAdd(guildId, pending))
            throw new InvalidOperationException(
                $"A voice handshake is already in progress for guild {guildId}."
            );

        try
        {
            await SendVoiceStateUpdateAsync(discord, guildId, channelId).ConfigureAwait(false);

            var stateTask = pending.State.Task;
            var serverTask = pending.Server.Task;

            try
            {
                await Task.WhenAll(stateTask, serverTask)
                    .WaitAsync(_timeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new VoiceHandshakeException(
                    $"The voice handshake for guild {guildId} timed out after {_timeout.TotalSeconds:N0} seconds. "
                        + "Make sure the bot has permission to join the channel and that the guild voice states intent is enabled."
                );
            }
            catch (OperationCanceledException)
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

            return (stateTask.Result, serverTask.Result);
        }
        catch
        {
            // The join already went to Discord, leave or the bot parks in the channel with no player.
            try
            {
                await SendVoiceStateUpdateAsync(discord, guildId, null).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The gateway is gone too, nothing left to compensate.
            }

            throw;
        }
        finally
        {
            _pending.TryRemove(guildId, out _);
        }
    }

    public Task LeaveChannelAsync(DiscordClient discord, ulong guildId) =>
        SendVoiceStateUpdateAsync(discord, guildId, null);

    private static Task SendVoiceStateUpdateAsync(
        DiscordClient discord,
        ulong guildId,
        ulong? channelId
    )
    {
        var payload = new Dictionary<string, object?>
        {
            ["guild_id"] = guildId.ToString(),
            ["channel_id"] = channelId?.ToString(),
            ["self_mute"] = false,
            ["self_deaf"] = false,
        };

        // The only public DSharpPlus API that can send the voice state update (op 4), obsolete
        // only because it bypasses validation that no specialized voice method offers anyway.
#pragma warning disable CS0618
        return discord.SendPayloadAsync(GatewayOpCode.VoiceStateUpdate, payload);
#pragma warning restore CS0618
    }

    private sealed class PendingHandshake
    {
        public TaskCompletionSource<VoiceStateUpdateEventArgs> State { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<VoiceServerUpdateEventArgs> Server { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
