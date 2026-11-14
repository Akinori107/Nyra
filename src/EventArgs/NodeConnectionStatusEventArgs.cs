using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The connection status a node reported.</summary>
/// <remarks>
/// NodeLink only, and only while the session has a player. <see cref="Node.GetConnectionStatusAsync"/> works
/// without one.
/// </remarks>
public sealed class NodeConnectionStatusEventArgs : System.EventArgs
{
    /// <summary>
    /// The reported status. <see cref="Node.ConnectionStatus"/> may already be newer.
    /// </summary>
    public ConnectionStatus Status { get; }

    /// <summary>Initializes the event args with the reported status.</summary>
    /// <param name="status">The status the node reported.</param>
    public NodeConnectionStatusEventArgs(ConnectionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        Status = status;
    }
}
