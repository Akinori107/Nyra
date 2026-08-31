namespace Nyra.EventArgs;

public sealed class NodeConnectedEventArgs : System.EventArgs
{
    public Node Node { get; }

    public NodeConnectedEventArgs(Node node)
    {
        Node = node;
    }
}
