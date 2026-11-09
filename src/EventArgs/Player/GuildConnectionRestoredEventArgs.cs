namespace Nyra.EventArgs;

/// <summary>
/// Raised when the node came back with a session it could not resume and this guild's connection was restored on
/// it, the bot never left the voice channel.
/// </summary>
/// <remarks>
/// Nyra sent the voice credentials it kept to the new session, so the player is new and empty, the track, the
/// position, the volume and the filters of the old session are gone, start playing again. When the restore
/// fails the connection is invalidated instead, see <see cref="GuildConnection.GuildConnectionInvalidated"/>.
/// </remarks>
/// <param name="guildId">The guild whose connection was restored.</param>
/// <param name="channelId">The voice channel the connection stayed in.</param>
/// <param name="playbackResent">Whether the track that was playing was sent to the node again.</param>
public sealed class GuildConnectionRestoredEventArgs(
    ulong guildId,
    ulong channelId,
    bool playbackResent
) : System.EventArgs
{
    /// <summary>The guild whose connection was restored.</summary>
    public ulong GuildId { get; } = guildId;

    /// <summary>The voice channel the connection stayed in.</summary>
    public ulong ChannelId { get; } = channelId;

    /// <summary>
    /// Whether the track that was playing was sent to the node again, see <see
    /// cref="NodeOptions.RescuePlayback"/>.
    /// </summary>
    /// <remarks>False when the option is off, nothing was playing, or sending the track failed.</remarks>
    public bool PlaybackResent { get; } = playbackResent;
}
