namespace Nyra.Abstractions;

/// <summary>Optional bridge capability, reports when the bot was removed from a guild.</summary>
/// <remarks>
/// A removal ends every voice connection without any voice state update, so without this notification the
/// connection stays registered forever and a later join of the re-invited bot hands out the dead one.
/// Implemented by the built-in bridges, consumed opportunistically.
/// <para>
/// The guild delete dispatch is gated on the guilds intent, a setup running without it silences this
/// notification entirely, a kick turns the connection into a zombie.
/// </para>
/// <para>
/// Handlers run inline on the library's gateway dispatch thread, the raise isolates a throwing handler,
/// not a blocking one, a subscriber that stalls here delays every gateway event after it, the voice states
/// of other guilds included.
/// </para>
/// </remarks>
public interface IGuildRemovalSource
{
    /// <summary>
    /// Raised when the bot was removed from a guild, on a best-effort basis, not raised for guild outages. The
    /// cache-backed bridges (Discord.Net, DSharpPlus) also skip a guild their cache no longer holds, the
    /// NetCord bridge raises unconditionally from the raw payload.
    /// </summary>
    event EventHandler<GuildRemovedEventArgs>? GuildRemoved;
}
