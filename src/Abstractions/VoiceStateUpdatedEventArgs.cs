namespace Nyra.Abstractions;

/// <summary>The voice state of the current user changed in a guild.</summary>
/// <remarks>
/// The channel id is wire-sourced on NetCord and the DSharpPlus 5.0 nightly line, cache-derived on DSharpPlus
/// 4.x and Discord.Net, a null channel id there also covers a channel their cache has not resolved,
/// indistinguishable from a leave.
/// </remarks>
/// <param name="guildId">The id of the guild the voice state belongs to.</param>
/// <param name="voiceState">The voice state of the current user after the change.</param>
public sealed class VoiceStateUpdatedEventArgs(ulong guildId, DiscordVoiceState voiceState)
    : System.EventArgs
{
    /// <summary>The id of the guild the voice state belongs to.</summary>
    public ulong GuildId { get; } = guildId;

    /// <summary>The voice state of the current user after the change.</summary>
    public DiscordVoiceState VoiceState { get; } = voiceState;
}
