namespace Nyra.Abstractions;

/// <summary>A guild's voice server credentials.</summary>
public readonly record struct VoiceServer(string Token, string Endpoint);
