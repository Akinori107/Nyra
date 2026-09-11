using global::NetCord.Gateway;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;
using VoiceState = Nyra.Abstractions.VoiceState;

namespace Nyra.NetCord;

/// <summary>
/// Base of the NetCord gateway bridges: translates the library's voice models into Nyra's.
/// </summary>
internal abstract class GatewayBridgeBase : IDiscordBridge, IDisposable
{
    private readonly ILogger? _logger;

    protected GatewayBridgeBase(ILogger? logger)
    {
        _logger = logger;
    }

    public event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated;
    public event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

    /// <inheritdoc />
    public ILogger? Logger => _logger;

    /// <inheritdoc />
    public async Task SendVoiceUpdateAsync(
        ulong guildId,
        ulong? channelId,
        bool selfDeaf = false,
        bool selfMute = false,
        CancellationToken cancellationToken = default
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

    /// <inheritdoc />
    public abstract Task<ClientInformation> WaitForReadyAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>Disconnects the bridge from the client's events.</summary>
    public abstract void Dispose();

    protected abstract GatewayClient GetClient(ulong guildId);

    protected ValueTask HandleVoiceServerUpdateAsync(VoiceServerUpdateEventArgs eventArgs)
    {
        // A null endpoint means the update is partial, the bridge's contract is a non-null one.
        if (eventArgs.Endpoint is null)
            return ValueTask.CompletedTask;

        VoiceServerUpdated?.Invoke(
            this,
            new VoiceServerUpdatedEventArgs(
                eventArgs.GuildId,
                new VoiceServer(eventArgs.Token, eventArgs.Endpoint)
            )
        );
        return ValueTask.CompletedTask;
    }

    protected ValueTask HandleVoiceStateUpdateAsync(
        global::NetCord.Gateway.VoiceState eventArgs,
        ulong currentUserId
    )
    {
        if (eventArgs.UserId != currentUserId)
            return ValueTask.CompletedTask;

        VoiceStateUpdated?.Invoke(
            this,
            new VoiceStateUpdatedEventArgs(
                eventArgs.GuildId,
                new VoiceState(eventArgs.ChannelId, eventArgs.SessionId)
            )
        );
        return ValueTask.CompletedTask;
    }
}
