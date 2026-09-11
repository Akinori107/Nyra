using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The connection status a node reported. NodeLink only.</summary>
public sealed class NodeConnectionStatusEventArgs : System.EventArgs
{
    /// <summary>The reported status, also the node's current <see cref="Node.ConnectionStatus"/>.</summary>
    public ConnectionStatus Status { get; }

    public NodeConnectionStatusEventArgs(ConnectionStatus status)
    {
        Status = status;
    }
}
