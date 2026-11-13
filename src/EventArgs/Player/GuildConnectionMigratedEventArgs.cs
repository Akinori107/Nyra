namespace Nyra.EventArgs;

/// <summary>
/// Raised when this guild's node stayed down for <see cref="NodeOptions.FailoverAfter"/> and the connection moved
/// to another node, the bot never left the voice channel.
/// </summary>
/// <remarks>
/// The old connection is torn down and every call on it throws, subscribe to the events of <see
/// cref="Connection"/>. The <see cref="Queue.GuildQueue"/> moves with it.
/// </remarks>
/// <param name="connection">The connection on the new node.</param>
/// <param name="from">The node that went down.</param>
/// <param name="playbackResent">Whether the track that was playing was sent to the new node.</param>
public sealed class GuildConnectionMigratedEventArgs(
    GuildConnection connection,
    Node from,
    bool playbackResent
) : System.EventArgs
{
    /// <summary>The guild whose connection moved.</summary>
    public ulong GuildId => Connection.GuildId;

    /// <summary>The connection on the new node.</summary>
    public GuildConnection Connection { get; } = connection;

    /// <summary>The node that went down.</summary>
    public Node From { get; } = from;

    /// <summary>The node the connection moved to.</summary>
    public Node To => Connection.Node;

    /// <summary>Whether the track that was playing was sent to the new node.</summary>
    /// <remarks>False when nothing was playing or sending the track failed.</remarks>
    public bool PlaybackResent { get; } = playbackResent;
}
