namespace Nyra.Abstractions;

/// <summary>A Discord voice server the audio node connects to on the bot's behalf for a guild.</summary>
/// <remarks>
/// An update with a blank token or endpoint is ignored.
/// </remarks>
/// <param name="Token">The token that authenticates the voice connection the audio node opens.</param>
/// <param name="Endpoint">
/// The address of the voice server, the bare host Discord reports, with no scheme or port.
/// </param>
public readonly record struct VoiceServer(string Token, string Endpoint);
