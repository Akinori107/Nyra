namespace Nyra.EventArgs;

/// <summary>
/// Raised when this guild's player is gone for good, the node's session could not be resumed and restoring the
/// connection on the new session failed (see <see cref="GuildConnection.GuildConnectionRestored"/>), or a
/// NodeLink cluster worker crashed and took the player with it.
/// </summary>
/// <remarks>
/// A node that gave up reconnecting does not raise this per guild, watch <see cref="Node.Disconnected"/> with
/// <see cref="NodeDisconnectedEventArgs.Final"/> for that. A rejoin returns a new connection, so subscribe to
/// its events again.
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
    /// Use this to rejoin, the connection's own channel already reads null.
    /// </remarks>
    public ulong? LastChannelId { get; } = lastChannelId;
}
