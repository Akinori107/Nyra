using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The connection status a node reported.</summary>
/// <remarks>
/// NodeLink only, and only while the node's session holds at least one player, a playerless session
/// receives no pushes, <see cref="Node.GetConnectionStatusAsync"/> works without one.
/// </remarks>
public sealed class NodeConnectionStatusEventArgs : System.EventArgs
{
    /// <summary>
    /// The reported status, the snapshot <see cref="Node.ConnectionStatus"/> held when the raise was queued.
    /// </summary>
    /// <remarks>A newer push may have replaced the property by handler time.</remarks>
    public ConnectionStatus Status { get; }

    /// <summary>Initializes the event args with the reported status.</summary>
    /// <param name="status">The status the node reported.</param>
    public NodeConnectionStatusEventArgs(ConnectionStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        Status = status;
    }
}
