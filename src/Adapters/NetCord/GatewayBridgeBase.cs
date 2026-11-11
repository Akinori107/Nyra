using NetCord;
using NetCord.Gateway;
using Nyra.Abstractions;

namespace Nyra.NetCord;

internal abstract class GatewayBridgeBase(
    Action<VoiceServerUpdatedEventArgs> onVoiceServerUpdated,
    Action<VoiceStateUpdatedEventArgs> onVoiceStateUpdated,
    Action<GuildRemovedEventArgs> onGuildRemoved,
    Action<ListenerVoiceStateUpdatedEventArgs> onListenerVoiceStateUpdated
) : IDisposable
{
    private readonly TaskCompletionSource<ClientInformation> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    public async Task SendVoiceUpdateAsync(
        ulong guildId,
        ulong? channelId,
        bool selfDeaf,
        bool selfMute,
        CancellationToken cancellationToken
    )
    {
        var properties = new VoiceStateProperties(guildId, channelId)
        {
            SelfDeaf = selfDeaf,
            SelfMute = selfMute,
        };

        await GetClient(guildId)
            .UpdateVoiceStateAsync(properties, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public IReadOnlyCollection<ulong>? GetListeners(ulong guildId, ulong channelId)
    {
        var client = GetClient(guildId);
        if (!client.Cache.Guilds.TryGetValue(guildId, out var guild))
            return null;

        return
        [
            .. guild
                .VoiceStates.Values.Where(state =>
                    state.ChannelId == channelId
                    && state.UserId != client.Id
                    && !IsBot(guild, state.UserId, state.User)
                )
                .Select(state => state.UserId),
        ];
    }

    public Task<ClientInformation> WaitForReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _ready.Task.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        try
        {
            Unsubscribe();
        }
        catch (ObjectDisposedException) { }
        _ready.TrySetCanceled();
    }

    protected abstract void Unsubscribe();

    protected abstract GatewayClient GetClient(ulong guildId);

    protected void SetReady(ulong currentUserId) =>
        _ready.TrySetResult(new ClientInformation(currentUserId));

    protected ValueTask MarkReady(ulong currentUserId)
    {
        SetReady(currentUserId);
        return ValueTask.CompletedTask;
    }

    protected ValueTask HandleDisconnectAsync(DisconnectEventArgs eventArgs)
    {
        if (!eventArgs.Reconnect)
            _ready.TrySetException(
                new InvalidOperationException(
                    "The gateway closed without a reconnect (a fatal close code), no ready will follow."
                )
            );
        return ValueTask.CompletedTask;
    }

    protected ValueTask HandleVoiceServerUpdateAsync(VoiceServerUpdateEventArgs eventArgs)
    {
        if (
            string.IsNullOrWhiteSpace(eventArgs.Endpoint)
            || string.IsNullOrWhiteSpace(eventArgs.Token)
        )
            return ValueTask.CompletedTask;

        onVoiceServerUpdated(
            new VoiceServerUpdatedEventArgs(
                eventArgs.GuildId,
                new VoiceServer(eventArgs.Token, eventArgs.Endpoint)
            )
        );
        return ValueTask.CompletedTask;
    }

    protected ValueTask HandleVoiceStateUpdateAsync(VoiceState eventArgs, ulong currentUserId)
    {
        if (eventArgs.GuildId is 0)
            return ValueTask.CompletedTask;

        if (eventArgs.UserId != currentUserId)
        {
            if (eventArgs.User?.IsBot != true)
                onListenerVoiceStateUpdated(
                    new ListenerVoiceStateUpdatedEventArgs(
                        eventArgs.GuildId,
                        eventArgs.UserId,
                        eventArgs.ChannelId
                    )
                );
            return ValueTask.CompletedTask;
        }

        onVoiceStateUpdated(
            new VoiceStateUpdatedEventArgs(
                eventArgs.GuildId,
                new DiscordVoiceState(
                    eventArgs.ChannelId,
                    eventArgs.SessionId,
                    eventArgs.IsSelfDeafened,
                    eventArgs.IsSelfMuted
                )
            )
        );
        return ValueTask.CompletedTask;
    }

    private static bool IsBot(Guild guild, ulong userId, GuildUser? user) =>
        user?.IsBot ?? (guild.Users.TryGetValue(userId, out var cached) && cached.IsBot);

    protected ValueTask HandleGuildDeleteAsync(GuildDeleteEventArgs eventArgs)
    {
        if (eventArgs.IsUnavailable)
            return ValueTask.CompletedTask;

        onGuildRemoved(new GuildRemovedEventArgs(eventArgs.GuildId));
        return ValueTask.CompletedTask;
    }
}
