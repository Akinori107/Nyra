namespace Nyra.EventArgs;

/// <summary>A node's websocket connected.</summary>
public sealed class NodeConnectedEventArgs : System.EventArgs
{
    /// <summary>The node that connected.</summary>
    public Node Node { get; }

    /// <summary>Initializes the event args with the node that connected.</summary>
    /// <param name="node">The node that connected.</param>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is null.</exception>
    public NodeConnectedEventArgs(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        Node = node;
    }
}
