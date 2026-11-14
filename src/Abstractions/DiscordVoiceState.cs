namespace Nyra.Abstractions;

/// <summary>
/// The current user's voice state in a guild, a null channel id means not connected.
/// </summary>
/// <remarks>
/// The cache-backed bridges (DSharpPlus 4.x, Discord.Net) also report null for a channel their cache has not
/// resolved, indistinguishable from a leave.
/// </remarks>
/// <param name="ChannelId">
/// The voice channel the user sits in, null when not connected, or not resolved yet on a cache-backed bridge.
/// </param>
/// <param name="SessionId">
/// The id of the voice session this state belongs to, null when the state carried no session id. On a leave it
/// is the id of the session that just ended.
/// </param>
/// <param name="SelfDeaf">
/// Whether the user deafened itself, null when the bridge cannot see the flag. Nyra keeps it when it rejoins
/// after the voice connection drops.
/// </param>
/// <param name="SelfMute">
/// Whether the user muted itself, null when the bridge cannot see the flag. Nyra keeps it when it rejoins after
/// the voice connection drops.
/// </param>
public readonly record struct DiscordVoiceState(
    ulong? ChannelId,
    string? SessionId,
    bool? SelfDeaf = null,
    bool? SelfMute = null
);
