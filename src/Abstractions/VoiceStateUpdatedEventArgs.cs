namespace Nyra.Abstractions;

/// <summary>The voice state of the current user changed in a guild.</summary>
/// <remarks>
/// The channel id is wire-sourced on NetCord and the DSharpPlus 5.0 nightly line, cache-derived on DSharpPlus
/// 4.x and Discord.Net, a null channel id there also covers a channel their cache has not resolved,
/// indistinguishable from a leave.
/// </remarks>
public sealed class VoiceStateUpdatedEventArgs : System.EventArgs
{
    /// <summary>Initializes the event args with the reported values.</summary>
    /// <param name="guildId">The id of the guild the voice state belongs to.</param>
    /// <param name="voiceState">The voice state of the current user after the change.</param>
    public VoiceStateUpdatedEventArgs(ulong guildId, DiscordVoiceState voiceState)
    {
        GuildId = guildId;
        VoiceState = voiceState;
    }

    /// <summary>The id of the guild the voice state belongs to.</summary>
    public ulong GuildId { get; }

    /// <summary>The voice state of the current user after the change.</summary>
    public DiscordVoiceState VoiceState { get; }
}
