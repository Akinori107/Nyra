namespace Nyra.Abstractions;

/// <summary>A Discord voice server the audio node connects to on the bot's behalf for a guild.</summary>
/// <remarks>
/// The values are always the complete pair, the core discards a partial update (a blank endpoint or token)
/// before it reaches a handshake. A <c>default</c> instance bypasses the members' annotations and carries
/// nulls, the same filters treat it as partial.
/// </remarks>
/// <param name="Token">The token that authenticates the voice connection the audio node opens.</param>
/// <param name="Endpoint">
/// The address of the voice server, the bare host Discord reports, with no scheme or port.
/// </param>
public readonly record struct VoiceServer(string Token, string Endpoint);
