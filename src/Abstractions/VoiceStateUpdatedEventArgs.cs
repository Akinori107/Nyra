namespace Nyra.Abstractions;

/// <summary>The voice state of the current user changed in a guild.</summary>
public sealed class VoiceStateUpdatedEventArgs : System.EventArgs
{
    public VoiceStateUpdatedEventArgs(ulong guildId, VoiceState voiceState)
    {
        GuildId = guildId;
        VoiceState = voiceState;
    }

    /// <summary>The guild the voice state belongs to.</summary>
    public ulong GuildId { get; }

    /// <summary>The voice state of the current user after the change.</summary>
    public VoiceState VoiceState { get; }
}
