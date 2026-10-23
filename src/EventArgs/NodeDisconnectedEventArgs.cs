using System.Net.WebSockets;

namespace Nyra.EventArgs;

/// <summary>
/// A node's websocket disconnected, or a startup that never connected gave up, or the supervision loop
/// itself failed (the node stops reconnecting on its own, restart it manually), the same event covers all
/// three, a manual stop raises no event.
/// </summary>
public sealed class NodeDisconnectedEventArgs : System.EventArgs
{
    /// <summary>The close status the websocket reported, null when none.</summary>
    /// <remarks>
    /// The raw close code may not be a named member of <see cref="WebSocketCloseStatus"/>, servers and
    /// proxies may close with application-defined codes (4000-4999). On a <see cref="Final"/> disconnect it
    /// still describes the last websocket drop, not the reconnect failures that led the node to give up,
    /// <see cref="Exception"/> carries those.
    /// </remarks>
    public WebSocketCloseStatus? CloseStatus { get; }

    /// <summary>The close description the websocket reported.</summary>
    /// <remarks>
    /// On a <see cref="Final"/> disconnect it still describes the last websocket drop, not the reconnect
    /// failures that led the node to give up, <see cref="Exception"/> carries those.
    /// <para>
    /// May carry a library-generated abort reason instead (e.g. <c>ready timeout</c>) when the connection was
    /// torn down locally, no server sent it.
    /// </para>
    /// </remarks>
    public string? CloseDescription { get; }

    /// <summary>
    /// True when the node stopped reconnecting and is no longer connected, the last event of a reconnect
    /// give-up, of a failed startup, or of a supervision loop that failed.
    /// </summary>
    /// <remarks>
    /// Not absolute, the slot is free and a manual restart reconnects, and a manual stop during the backoff
    /// can end the reconnect loop without a final event.
    /// </remarks>
    public bool Final { get; }

    /// <summary>
    /// The exception that best describes the disconnect, the websocket layer's for the drop, the library's
    /// ready-timeout when it tore the connection down locally (see <see cref="CloseDescription"/>), or the
    /// failure of the last reconnect attempt, the initial connect's when none ran, on a <see cref="Final"/>
    /// disconnect.
    /// </summary>
    /// <remarks>Null when the websocket closed cleanly and no attempt failed.</remarks>
    public Exception? Exception { get; }

    /// <summary>Initializes the event args with the reported values.</summary>
    /// <param name="closeStatus">The status the websocket closed with, null when none.</param>
    /// <param name="closeDescription">The description the websocket closed with.</param>
    /// <param name="final">
    /// Whether this disconnect is final, false while the node still tries or could try to reconnect.
    /// </param>
    /// <param name="exception">The exception that best describes the disconnect, null when none is known.</param>
    public NodeDisconnectedEventArgs(
        WebSocketCloseStatus? closeStatus,
        string? closeDescription,
        bool final,
        Exception? exception = null
    )
    {
        CloseStatus = closeStatus;
        CloseDescription = closeDescription;
        Final = final;
        Exception = exception;
    }
}
