using NetCord.Gateway;
using Nyra.Abstractions;

namespace Nyra.NetCord;

internal abstract class GatewayBridgeBase : IDisposable
{
    private readonly Action<VoiceServerUpdatedEventArgs> _onVoiceServerUpdated;
    private readonly Action<VoiceStateUpdatedEventArgs> _onVoiceStateUpdated;
    private readonly Action<GuildRemovedEventArgs> _onGuildRemoved;
    private readonly TaskCompletionSource<ClientInformation> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    protected GatewayBridgeBase(
        Action<VoiceServerUpdatedEventArgs> onVoiceServerUpdated,
        Action<VoiceStateUpdatedEventArgs> onVoiceStateUpdated,
        Action<GuildRemovedEventArgs> onGuildRemoved
    )
    {
        _onVoiceServerUpdated = onVoiceServerUpdated;
        _onVoiceStateUpdated = onVoiceStateUpdated;
        _onGuildRemoved = onGuildRemoved;
    }

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

    protected ValueTask MarkReady(ulong currentUserId)
    {
        _ready.TrySetResult(new ClientInformation(currentUserId));
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

        _onVoiceServerUpdated(
            new VoiceServerUpdatedEventArgs(
                eventArgs.GuildId,
                new VoiceServer(eventArgs.Token, eventArgs.Endpoint)
            )
        );
        return ValueTask.CompletedTask;
    }

    protected ValueTask HandleVoiceStateUpdateAsync(VoiceState eventArgs, ulong currentUserId)
    {
        if (eventArgs.UserId != currentUserId)
            return ValueTask.CompletedTask;

        if (eventArgs.GuildId is 0)
            return ValueTask.CompletedTask;

        _onVoiceStateUpdated(
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

    protected ValueTask HandleGuildDeleteAsync(GuildDeleteEventArgs eventArgs)
    {
        if (eventArgs.IsUnavailable)
            return ValueTask.CompletedTask;

        _onGuildRemoved(new GuildRemovedEventArgs(eventArgs.GuildId));
        return ValueTask.CompletedTask;
    }
}
