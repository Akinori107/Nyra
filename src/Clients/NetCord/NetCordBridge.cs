using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using Nyra.Abstractions;

namespace Nyra.NetCord;

/// <summary>Wraps a <see cref="GatewayClient"/> or <see cref="ShardedGatewayClient"/> for Nyra.</summary>
/// <remarks>
/// NetCord logs through its own <c>IGatewayLogger</c>, configured only at client creation and not exposed
/// afterwards, so the logger here is one the caller provides.
/// </remarks>
public sealed class NetCordBridge : IDiscordBridge, IGuildRemovalSource, IDisposable
{
    private readonly GatewayBridgeBase _client;
    private readonly ILogger? _logger;

    /// <summary>
    /// Raised when Discord allocates or re-targets the voice server of a guild, with this bridge as the
    /// sender.
    /// </summary>
    public event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated;

    /// <summary>
    /// Raised when the voice state of the current user changes in a guild, with this bridge as the sender.
    /// </summary>
    public event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

    /// <summary>Raised when the bot was removed from a guild, with this bridge as the sender.</summary>
    public event EventHandler<GuildRemovedEventArgs>? GuildRemoved;

    /// <summary>Wraps a <see cref="GatewayClient"/>, forwarding its voice events.</summary>
    /// <param name="client">The client to wrap.</param>
    /// <param name="logger">The logger to write the bridge's messages to.</param>
    public NetCordBridge(GatewayClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _logger = logger;
        _client = new GatewayBridge(
            client,
            RaiseVoiceServerUpdated,
            RaiseVoiceStateUpdated,
            RaiseGuildRemoved
        );
    }

    /// <summary>Wraps a <see cref="ShardedGatewayClient"/>, forwarding the voice events of every shard.</summary>
    /// <param name="client">The sharded client to wrap.</param>
    /// <param name="logger">The logger to write the bridge's messages to.</param>
    public NetCordBridge(ShardedGatewayClient client, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _logger = logger;
        _client = new ShardedGatewayBridge(
            client,
            RaiseVoiceServerUpdated,
            RaiseVoiceStateUpdated,
            RaiseGuildRemoved
        );
    }

    private void RaiseVoiceServerUpdated(VoiceServerUpdatedEventArgs eventArgs) =>
        BridgeEvent.RaiseIsolated(VoiceServerUpdated, this, eventArgs, _logger);

    private void RaiseVoiceStateUpdated(VoiceStateUpdatedEventArgs eventArgs) =>
        BridgeEvent.RaiseIsolated(VoiceStateUpdated, this, eventArgs, _logger);

    private void RaiseGuildRemoved(GuildRemovedEventArgs eventArgs) =>
        BridgeEvent.RaiseIsolated(GuildRemoved, this, eventArgs, _logger);

    /// <inheritdoc />
    public ILogger? Logger => _logger;

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
