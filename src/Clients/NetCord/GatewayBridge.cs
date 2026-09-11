using global::NetCord.Gateway;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;

namespace Nyra.NetCord;

/// <summary>Bridges a single <see cref="GatewayClient"/> for Nyra.</summary>
internal sealed class GatewayBridge : GatewayBridgeBase
{
    private readonly GatewayClient _client;
    private readonly TaskCompletionSource<ClientInformation> _ready;

    public GatewayBridge(GatewayClient client, ILogger? logger)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
        _ready = new TaskCompletionSource<ClientInformation>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        client.VoiceStateUpdate += OnVoiceStateUpdate;
        client.VoiceServerUpdate += OnVoiceServerUpdate;
        client.Ready += OnClientReady;

        if (client.Status is WebSocketStatus.Ready)
            _ready.TrySetResult(new ClientInformation("NetCord", client.Id));
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
        _client.Ready -= OnClientReady;
    }

    protected override GatewayClient GetClient(ulong guildId) => _client;

    private ValueTask OnClientReady(ReadyEventArgs eventArgs)
    {
        _ready.TrySetResult(new ClientInformation("NetCord", _client.Id));
        return ValueTask.CompletedTask;
    }

    private ValueTask OnVoiceStateUpdate(global::NetCord.Gateway.VoiceState eventArgs)
    {
        return HandleVoiceStateUpdateAsync(eventArgs, _client.Id);
    }

    private ValueTask OnVoiceServerUpdate(VoiceServerUpdateEventArgs eventArgs)
    {
        return HandleVoiceServerUpdateAsync(eventArgs);
    }
}
