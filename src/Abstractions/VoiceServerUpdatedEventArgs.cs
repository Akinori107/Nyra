namespace Nyra.Abstractions;

/// <summary>The voice server of a guild was re-targeted.</summary>
public sealed class VoiceServerUpdatedEventArgs : System.EventArgs
{
    public VoiceServerUpdatedEventArgs(ulong guildId, VoiceServer voiceServer)
    {
        GuildId = guildId;
        VoiceServer = voiceServer;
    }

    /// <summary>The guild the voice server belongs to.</summary>
    public ulong GuildId { get; }

    /// <summary>The new voice server credentials.</summary>
    public VoiceServer VoiceServer { get; }
}
