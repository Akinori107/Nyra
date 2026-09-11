using global::NetCord.Gateway;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;

namespace Nyra.NetCord;

/// <summary>Bridges a <see cref="ShardedGatewayClient"/> for Nyra.</summary>
internal sealed class ShardedGatewayBridge : GatewayBridgeBase
{
    private readonly ShardedGatewayClient _client;
    private readonly TaskCompletionSource<ClientInformation> _ready;

    public ShardedGatewayBridge(ShardedGatewayClient client, ILogger? logger)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _ready = new TaskCompletionSource<ClientInformation>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        client.VoiceStateUpdate += OnVoiceStateUpdate;
        client.VoiceServerUpdate += OnVoiceServerUpdate;
        client.Ready += OnShardReady;
    }

    public override Task<ClientInformation> WaitForReadyAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _ready.Task.WaitAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _client.VoiceStateUpdate -= OnVoiceStateUpdate;
        _client.VoiceServerUpdate -= OnVoiceServerUpdate;
        _client.Ready -= OnShardReady;
    }

    protected override GatewayClient GetClient(ulong guildId) => _client[guildId];

    private ValueTask OnShardReady(GatewayClient client, ReadyEventArgs eventArgs)
    {
        _ready.TrySetResult(new ClientInformation("NetCord", client.Id));
        return ValueTask.CompletedTask;
    }

    private ValueTask OnVoiceStateUpdate(
        GatewayClient client,
        global::NetCord.Gateway.VoiceState eventArgs
    )
    {
        return HandleVoiceStateUpdateAsync(eventArgs, client.Id);
    }

    private ValueTask OnVoiceServerUpdate(
        GatewayClient client,
        VoiceServerUpdateEventArgs eventArgs
    )
    {
        return HandleVoiceServerUpdateAsync(eventArgs);
    }
}
