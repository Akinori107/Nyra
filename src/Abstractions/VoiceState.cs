namespace Nyra.Abstractions;

/// <summary>The current user's voice state in a guild, a null channel id means not connected.</summary>
public readonly record struct VoiceState(ulong? ChannelId, string? SessionId);
