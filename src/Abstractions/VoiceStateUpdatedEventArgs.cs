namespace Nyra.Abstractions;

/// <summary>The voice state of the current user changed in a guild.</summary>
/// <remarks>
/// On DSharpPlus 4.x and Discord.Net a null channel id can also mean the cache did not resolve the channel.
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
