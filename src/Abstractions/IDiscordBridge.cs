using Microsoft.Extensions.Logging;

namespace Nyra.Abstractions;

/// <summary>
/// The Discord client contract Nyra runs on, implemented once per Discord library. Voice events
/// are only raised for the current user, the implementation filters other users out.
/// </summary>
public interface IDiscordBridge
{
    /// <summary>Raised when Discord re-targets the voice server of a guild.</summary>
    event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated;

    /// <summary>Raised when the voice state of the current user changes in a guild.</summary>
    event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

    /// <summary>The client's logger, or null when the library offers none.</summary>
    ILogger? Logger { get; }

    /// <summary>
    /// Sends the gateway voice state update (op 4): a channel id joins it, a null channel id
    /// leaves the voice channel again.
    /// </summary>
    Task SendVoiceUpdateAsync(
        ulong guildId,
        ulong? channelId,
        bool selfDeaf = false,
        bool selfMute = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Waits for the gateway to become ready and returns the client's identification,
    /// completing immediately when it already is.
    /// </summary>
    Task<ClientInformation> WaitForReadyAsync(CancellationToken cancellationToken = default);
}
