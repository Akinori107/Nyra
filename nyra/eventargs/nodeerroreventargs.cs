namespace Nyra.EventArgs;

/// <summary>Raised when an internal client error occurs: a malformed payload, a failed reconnect or a user handler that threw.</summary>
public sealed class NodeErrorEventArgs : System.EventArgs
{
    public Exception Exception { get; }

    public Node Node { get; }

    public NodeErrorEventArgs(Exception exception, Node node)
    {
        Exception = exception;
        Node = node;
    }
}
