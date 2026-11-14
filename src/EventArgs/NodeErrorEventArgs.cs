namespace Nyra.EventArgs;

/// <summary>
/// Raised for client errors, like bad payloads from the server, ready timeouts and failed REST calls.
/// </summary>
/// <remarks>
/// Reconnect failures are only logged, a handler that throws is caught and logged, and playback errors come
/// through the guild connection.
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
