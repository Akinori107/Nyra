using System.Net.WebSockets;

namespace Nyra.EventArgs;

public sealed class NodeDisconnectedEventArgs : System.EventArgs
{
    public WebSocketCloseStatus? CloseStatus { get; }

    public string? CloseDescription { get; }

    /// <summary>True when the node stopped reconnecting and is now permanently disconnected.</summary>
    public bool Final { get; }

    public NodeDisconnectedEventArgs(
        WebSocketCloseStatus? closeStatus,
        string? closeDescription,
        bool final
    )
    {
        CloseStatus = closeStatus;
        CloseDescription = closeDescription;
        Final = final;
    }
}
