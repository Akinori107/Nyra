namespace Nyra.Abstractions;

/// <summary>Identifies the Discord client behind a bridge.</summary>
public readonly record struct ClientInformation(ulong CurrentUserId);
