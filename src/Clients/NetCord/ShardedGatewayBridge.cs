using NetCord.Gateway;
using Nyra.Abstractions;

namespace Nyra.NetCord;

internal sealed class ShardedGatewayBridge : GatewayBridgeBase
{
    private readonly ShardedGatewayClient _client;

    public ShardedGatewayBridge(
        ShardedGatewayClient client,
        Action<VoiceServerUpdatedEventArgs> onVoiceServerUpdated,
        Action<VoiceStateUpdatedEventArgs> onVoiceStateUpdated,
        Action<GuildRemovedEventArgs> onGuildRemoved
    )
        : base(onVoiceServerUpdated, onVoiceStateUpdated, onGuildRemoved)
    {
        _client = client;

        client.VoiceStateUpdate += OnVoiceStateUpdate;
        client.VoiceServerUpdate += OnVoiceServerUpdate;
        client.Ready += OnShardReady;
        client.Resume += OnShardResume;
        client.GuildDelete += OnShardGuildDelete;
        client.Disconnect += OnShardDisconnect;

        foreach (var shard in client)
            if (shard.Status is WebSocketStatus.Ready)
            {
                MarkReady(client.Id);
                break;
            }
    }

    protected override void Unsubscribe()
    {
        _client.VoiceStateUpdate -= OnVoiceStateUpdate;
        _client.VoiceServerUpdate -= OnVoiceServerUpdate;
        _client.Ready -= OnShardReady;
        _client.Resume -= OnShardResume;
        _client.GuildDelete -= OnShardGuildDelete;
        _client.Disconnect -= OnShardDisconnect;
    }

    protected override GatewayClient GetClient(ulong guildId)
    {
        GatewayClient[] clients = [.. _client];
        if (clients.Length is 0)
            throw new InvalidOperationException(
                "The sharded client has no started shards, start it before sending voice updates."
            );

        var shard =
            clients[0].Shard
            ?? throw new InvalidOperationException(
                "The shard range of this sharded client is not available."
            );

        var index = GuildShard.IdFor(guildId, shard.Count) - shard.Id;
        if (index >= 0 && index < clients.Length)
        {
            if (clients[index].Status is WebSocketStatus.Disconnected)
                throw new InvalidOperationException(
                    $"The shard serving the guild {guildId} has not been started yet, start the sharded client and wait for its shards before joining."
                );
            return clients[index];
        }

        throw new InvalidOperationException(
            $"The guild {guildId} is served by a shard outside this client's shard range, the voice update cannot be sent from this client."
        );
    }

    private ValueTask OnShardDisconnect(GatewayClient client, DisconnectEventArgs eventArgs) =>
        HandleDisconnectAsync(eventArgs);

    private ValueTask OnShardReady(GatewayClient client, ReadyEventArgs eventArgs) =>
        MarkReady(client.Id);

    private ValueTask OnShardResume(GatewayClient client) => MarkReady(client.Id);

    private ValueTask OnVoiceStateUpdate(GatewayClient client, VoiceState eventArgs) =>
        HandleVoiceStateUpdateAsync(eventArgs, client.Id);

    private ValueTask OnVoiceServerUpdate(
        GatewayClient client,
        VoiceServerUpdateEventArgs eventArgs
    ) => HandleVoiceServerUpdateAsync(eventArgs);

    private ValueTask OnShardGuildDelete(GatewayClient client, GuildDeleteEventArgs eventArgs) =>
        HandleGuildDeleteAsync(eventArgs);
}
