namespace Nyra.Abstractions;

/// <summary>Identifies the Discord client behind a bridge.</summary>
/// <param name="CurrentUserId">The id of the user the client is logged in as.</param>
public readonly record struct ClientInformation(ulong CurrentUserId);
