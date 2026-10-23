namespace Nyra.EventArgs;

/// <summary>
/// Raised for internal client errors, malformed payloads from the server, ready timeouts, failed REST calls to
/// the node, gateway leave failures, filter chains the server keeps diverging from and fatal supervision
/// failures.
/// </summary>
/// <remarks>
/// Reconnect failures log separately, a throwing handler is caught and logged, playback errors surface through
/// the guild connection instead.
/// </remarks>
public sealed class NodeErrorEventArgs : System.EventArgs
{
    /// <summary>The exception describing the error.</summary>
    public Exception Exception { get; }

    /// <summary>The node the error was raised for.</summary>
    public Node Node { get; }

    /// <summary>Initializes the event args with the reported error.</summary>
    /// <param name="exception">The exception describing the error.</param>
    /// <param name="node">The node the error was raised for.</param>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> or <paramref name="node"/> is
    /// null.
    /// </exception>
    public NodeErrorEventArgs(Exception exception, Node node)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(node);
        Exception = exception;
        Node = node;
    }
}
