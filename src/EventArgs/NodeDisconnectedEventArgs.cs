using System.Net.WebSockets;

namespace Nyra.EventArgs;

/// <summary>
/// A node's websocket disconnected, a first connect gave up, or the reconnect loop failed. A manual stop
/// raises no event.
/// </summary>
/// <param name="closeStatus">The status the websocket closed with, null when none.</param>
/// <param name="closeDescription">The description the websocket closed with.</param>
/// <param name="final">
/// Whether this disconnect is final, false while the node still tries or could try to reconnect.
/// </param>
/// <param name="exception">The exception that best describes the disconnect, null when none is known.</param>
public sealed class NodeDisconnectedEventArgs(
    WebSocketCloseStatus? closeStatus,
    string? closeDescription,
    bool final,
    Exception? exception = null
) : System.EventArgs
{
    /// <summary>The close status the websocket reported, null when none.</summary>
    /// <remarks>
    /// May be a code outside <see cref="WebSocketCloseStatus"/>. On a <see cref="Final"/> disconnect it is the
    /// last drop, <see cref="Exception"/> has the reconnect failure.
    /// </remarks>
    public WebSocketCloseStatus? CloseStatus { get; } = closeStatus;

    /// <summary>The close description the websocket reported.</summary>
    /// <remarks>
    /// On a <see cref="Final"/> disconnect it is the last drop. When Nyra closed the connection itself, like on a
    /// ready timeout, it is Nyra's reason.
    /// </remarks>
    public string? CloseDescription { get; } = closeDescription;

    /// <summary>
    /// True when the node stopped reconnecting.
    /// </summary>
    /// <remarks>
    /// Connecting the node again still works. A manual stop can end the loop without a final event.
    /// </remarks>
    public bool Final { get; } = final;

    /// <summary>
    /// The exception behind the disconnect, the last reconnect failure on a <see cref="Final"/> one.
    /// </summary>
    /// <remarks>Null when the websocket closed cleanly and no attempt failed.</remarks>
    public Exception? Exception { get; } = exception;
}
