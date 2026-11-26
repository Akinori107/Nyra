using Microsoft.Extensions.Logging;

namespace Nyra.Abstractions;

/// <summary>The Discord client contract Nyra runs on, implemented once per Discord library.</summary>
/// <remarks>Voice events are only raised for the current user, the implementation filters other users out.</remarks>
public interface IDiscordBridge
{
    /// <summary>
    /// Raised when Discord sends a complete voice server update for a guild, on a join or when the server
    /// moves.
    /// </summary>
    /// <remarks>
    /// The sender must be this bridge instance, another sender breaks Nyra's tracking.
    /// </remarks>
    event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated;

    /// <summary>Raised when the voice state of the current user changes in a guild.</summary>
    /// <remarks>The sender rule of <see cref="VoiceServerUpdated"/> applies here too.</remarks>
    event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

    /// <summary>The bridge's logger, or null when neither the library nor the caller provides one.</summary>
    ILogger? Logger { get; }

    /// <summary>
    /// Sends the gateway voice state update (op 4), a channel id joins it, a null channel id leaves the voice
    /// channel again.
    /// </summary>
    /// <remarks>
    /// Implementations must honor the cancellation token as far as the library allows.
    /// </remarks>
    /// <param name="guildId">The guild to send the voice state update in.</param>
    /// <param name="channelId">The voice channel to join, null leaves the voice channel.</param>
    /// <param name="selfDeaf">
    /// Whether the update deafens the current user. It applies to joins, a leave may ignore it.
    /// </param>
    /// <param name="selfMute">
    /// Whether the update mutes the current user. It applies to joins, a leave may ignore it.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that completes when the update was sent, or dropped by the library during a gateway reconnect.
    /// </returns>
    Task SendVoiceUpdateAsync(
        ulong guildId,
        ulong? channelId,
        bool selfDeaf = false,
        bool selfMute = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns the client's identification once it is available, completing immediately when it already is.
    /// </summary>
    /// <remarks>
    /// <see cref="NyraExtension.ConnectAllNodesAsync"/> waits up to sixty seconds and then throws a
    /// TimeoutException. A gateway that closed for good, like a bad token, fails the wait right away.
    /// <para>
    /// Implementations must keep the first result and return it on every later call, and must cancel pending
    /// waits on dispose, since the node waits on this without a timeout.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The client's identification, once it is available. The node keeps the first one for its lifetime.
    /// </returns>
    Task<ClientInformation> WaitForReadyAsync(CancellationToken cancellationToken = default);
}
