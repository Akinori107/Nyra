namespace Nyra.EventArgs;

/// <summary>
/// Raised when this guild's player is gone for good, the node's session could not be resumed and restoring the
/// connection on the new session failed (see <see cref="GuildConnection.GuildConnectionRestored"/>), or a
/// NodeLink cluster worker crashed and took the player with it.
/// </summary>
/// <remarks>
/// A node that gave up reconnecting raises no per-guild event, its guild connections stay registered until
/// the node connects again (unless a Discord-side removal during the outage tears one down with no event),
/// so watch <see cref="Node.Disconnected"/> with <see cref="NodeDisconnectedEventArgs.Final"/> for that mode
/// of loss.
/// <para>
/// The subscription is per instance. A rejoin inside the handler creates a successor connection whose events
/// have no handlers, so subscribe to the returned connection after a reconnect.
/// </para>
/// </remarks>
/// <param name="guildId">The guild whose connection was invalidated.</param>
/// <param name="lastChannelId">The voice channel the connection last played in, null when none is known.</param>
public sealed class GuildConnectionInvalidatedEventArgs(ulong guildId, ulong? lastChannelId)
    : System.EventArgs
{
    /// <summary>The guild whose connection was invalidated.</summary>
    public ulong GuildId { get; } = guildId;

    /// <summary>
    /// The voice channel the connection was last connected to, null when none is known.
    /// </summary>
    /// <remarks>
    /// Snapshotted before the invalidation wipes the connection's state, by the time a handler runs, the
    /// sender's own channel reads null, this is the channel to rejoin.
    /// </remarks>
    public ulong? LastChannelId { get; } = lastChannelId;
}
