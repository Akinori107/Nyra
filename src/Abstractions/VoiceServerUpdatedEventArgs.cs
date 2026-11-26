namespace Nyra.Abstractions;

/// <summary>The voice server of a guild was allocated or re-targeted.</summary>
/// <remarks>
/// Bridges raise it only with both the token and the endpoint. The endpoint is the bare host Discord reports,
/// with no scheme or port.
/// </remarks>
/// <param name="guildId">The id of the guild the voice server belongs to.</param>
/// <param name="voiceServer">The new voice server credentials, a complete token and endpoint pair.</param>
public sealed class VoiceServerUpdatedEventArgs(ulong guildId, VoiceServer voiceServer)
    : System.EventArgs
{
    /// <summary>The id of the guild the voice server belongs to.</summary>
    public ulong GuildId { get; } = guildId;

    /// <summary>The new voice server credentials.</summary>
    public VoiceServer VoiceServer { get; } = voiceServer;
}
