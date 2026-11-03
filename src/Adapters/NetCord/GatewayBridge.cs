using NetCord.Gateway;
using Nyra.Abstractions;

namespace Nyra.NetCord;

internal sealed class GatewayBridge : GatewayBridgeBase
{
    private readonly GatewayClient _client;

    public GatewayBridge(
        GatewayClient client,
        Action<VoiceServerUpdatedEventArgs> onVoiceServerUpdated,
        Action<VoiceStateUpdatedEventArgs> onVoiceStateUpdated,
        Action<GuildRemovedEventArgs> onGuildRemoved
    )
        : base(onVoiceServerUpdated, onVoiceStateUpdated, onGuildRemoved)
    {
        _client = client;

        client.VoiceStateUpdate += OnVoiceStateUpdate;
        client.VoiceServerUpdate += HandleVoiceServerUpdateAsync;
        client.Ready += OnClientReady;
        client.Resume += OnClientResume;
        client.GuildDelete += HandleGuildDeleteAsync;
        client.Disconnect += HandleDisconnectAsync;

        if (client.Status is WebSocketStatus.Ready)
            MarkReady(client.Id);
    }

    protected override void Unsubscribe()
    {
        _client.VoiceStateUpdate -= OnVoiceStateUpdate;
        _client.VoiceServerUpdate -= HandleVoiceServerUpdateAsync;
        _client.Ready -= OnClientReady;
        _client.Resume -= OnClientResume;
        _client.GuildDelete -= HandleGuildDeleteAsync;
        _client.Disconnect -= HandleDisconnectAsync;
    }

    protected override GatewayClient GetClient(ulong guildId)
    {
        if (
            _client.Shard is { Count: > 1 } shard
            && GuildShard.IdFor(guildId, shard.Count) != shard.Id
        )
            throw new InvalidOperationException(
                $"The guild {guildId} is not served by shard {shard.Id} of this client, use the client of the guild's own shard."
            );

        return _client;
    }

    private ValueTask OnClientReady(ReadyEventArgs eventArgs) => MarkReady(_client.Id);

    private ValueTask OnClientResume() => MarkReady(_client.Id);

    private ValueTask OnVoiceStateUpdate(VoiceState eventArgs) =>
        HandleVoiceStateUpdateAsync(eventArgs, _client.Id);
}
