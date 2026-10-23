namespace Nyra.Abstractions;

/// <summary>The voice server of a guild was allocated or re-targeted.</summary>
/// <remarks>
/// The contract is a complete update, the bridge raises it only with both the token and the endpoint present,
/// a partial update (the undocumented null or blank variants) is discarded by the core instead of completing a
/// handshake. The endpoint is the bare host Discord reports, with no scheme or port.
/// </remarks>
public sealed class VoiceServerUpdatedEventArgs : System.EventArgs
{
    /// <summary>Initializes the event args with the reported values.</summary>
    /// <param name="guildId">The id of the guild the voice server belongs to.</param>
    /// <param name="voiceServer">The new voice server credentials, a complete token and endpoint pair.</param>
    public VoiceServerUpdatedEventArgs(ulong guildId, VoiceServer voiceServer)
    {
        GuildId = guildId;
        VoiceServer = voiceServer;
    }

    /// <summary>The id of the guild the voice server belongs to.</summary>
    public ulong GuildId { get; }

    /// <summary>The new voice server credentials.</summary>
    public VoiceServer VoiceServer { get; }
}
