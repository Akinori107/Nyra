using Microsoft.Extensions.Logging;

namespace Nyra.Abstractions;

/// <summary>The Discord client contract Nyra runs on, implemented once per Discord library.</summary>
/// <remarks>Voice events are only raised for the current user, the implementation filters other users out.</remarks>
public interface IDiscordBridge
{
    /// <summary>
    /// Raised when Discord sends a complete voice server update for a guild, the allocation a join handshake
    /// waits on, or the re-targeting of a live connection's server (move or failover).
    /// </summary>
    /// <remarks>
    /// The sender must be the subscribed bridge instance itself, consumers key echo state by that instance,
    /// and a forwarding implementation that raises with another bridge as the sender (the exact trap NetCord's
    /// bridge re-raises with <c>this</c> to avoid) silently breaks the echo tracking.
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
    /// Implementations must honor the cancellation token as far as the underlying library allows, a send the
    /// library cannot cancel is abandoned alive, its late join parks the bot in a channel it has no player
    /// for, and the handshake's timeout guards it.
    /// <para>
    /// An implementation whose library exposes no raw op-4 send resolves the join and the leave through the
    /// library's own audio state instead, so channel resolution and the library's audio locks apply to both.
    /// </para>
    /// </remarks>
    /// <param name="guildId">The guild to send the voice state update in.</param>
    /// <param name="channelId">The voice channel to join, null leaves the voice channel.</param>
    /// <param name="selfDeaf">
    /// Whether the update deafens the current user. The flags apply to joins. A leave may drop them where the
    /// library's disconnect route fixes its own (Discord.Net), the core's leaves carry the defaults and its
    /// rescue re-join replays the flags the gateway last confirmed.
    /// </param>
    /// <param name="selfMute">
    /// Whether the update mutes the current user. Like <paramref name="selfDeaf"/>, joins carry it, a leave
    /// may drop it, the leaves carry the defaults and the rescue re-join replays the observed flags.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that completes when the update was sent, or when the library dropped it (DSharpPlus during a
    /// gateway reconnect).
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
    /// The DSharpPlus and Discord.Net built-ins resolve it from the REST login, where the gateway may still be
    /// connecting (a bridge's sends are guarded on socket-up separately), NetCord waits for the gateway's
    /// READY or RESUME. The extension bounds its own wait at sixty seconds, a gateway that never becomes ready
    /// surfaces as a TimeoutException from <see cref="NyraExtension.ConnectAllNodesAsync"/>, a NetCord gateway
    /// that closed fatally (a bad token and friends) fails the wait fast instead and the close surfaces as the
    /// bridge's InvalidOperationException from the same call.
    /// <para>
    /// Implementations must remember the first completion, later calls complete synchronously with the same
    /// identification, even when the gateway has dropped again in between, the node's own wait carries no
    /// bound of its own.
    /// </para>
    /// <para>
    /// Implementations must fail pending waits on dispose (TaskCanceledException), a wait left pending would
    /// hang the unbounded node wait after it.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The client's identification, once it is available. The first completion is what the node keeps for its
    /// connection's lifetime, a bridge whose identity changes mid-flight keeps reporting the first one.
    /// </returns>
    Task<ClientInformation> WaitForReadyAsync(CancellationToken cancellationToken = default);
}
