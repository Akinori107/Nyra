namespace Nyra.Abstractions;

/// <summary>Optional bridge capability, reports when the bot was removed from a guild.</summary>
/// <remarks>
/// Without it, a connection of a guild the bot was removed from stays around and a later join gets the dead
/// one. The built-in bridges implement it, and it needs the guilds intent.
/// <para>
/// Handlers run on the gateway thread, so a slow handler delays every gateway event after it.
/// </para>
/// </remarks>
public interface IGuildRemovalSource
{
    /// <summary>
    /// Raised when the bot was removed from a guild, not for guild outages. Best effort, a bridge may miss a
    /// guild its cache no longer holds.
    /// </summary>
    event EventHandler<GuildRemovedEventArgs>? GuildRemoved;
}
