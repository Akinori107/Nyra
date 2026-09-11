using global::NetCord.Gateway;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;

namespace Nyra.NetCord;

/// <summary>
/// Wraps a <see cref="GatewayClient"/> or <see cref="ShardedGatewayClient"/> for Nyra. NetCord
/// logs through its own <c>IGatewayLogger</c>, configured only at client creation and not
/// exposed afterwards, so the logger here is one the caller provides.
/// </summary>
public sealed class NetCordBridge : IDiscordBridge, IDisposable
{
    private readonly GatewayBridgeBase _client;

    public NetCordBridge(GatewayClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = new GatewayBridge(client, logger);
    }

    public NetCordBridge(ShardedGatewayClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = new ShardedGatewayBridge(client, logger);
    }

    public event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated
    {
        add => _client.VoiceServerUpdated += value;
        remove => _client.VoiceServerUpdated -= value;
    }

    public event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated
    {
        add => _client.VoiceStateUpdated += value;
        remove => _client.VoiceStateUpdated -= value;
    }

    /// <inheritdoc />
    public ILogger? Logger => _client.Logger;

    /// <inheritdoc />
    public Task SendVoiceUpdateAsync(
        ulong guildId,
        ulong? channelId,
        bool selfDeaf = false,
        bool selfMute = false,
        CancellationToken cancellationToken = default
    ) => _client.SendVoiceUpdateAsync(guildId, channelId, selfDeaf, selfMute, cancellationToken);

    /// <inheritdoc />
    public Task<ClientInformation> WaitForReadyAsync(
        CancellationToken cancellationToken = default
    ) => _client.WaitForReadyAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
