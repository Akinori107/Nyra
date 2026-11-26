using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>A node finished its ready handshake.</summary>
public sealed class NodeReadyEventArgs : System.EventArgs
{
    /// <summary>Whether the node is running a resumed session.</summary>
    /// <remarks>
    /// Checked against the session id Nyra sent, so a resume the server cannot back up reads as false.
    /// <see cref="ServerClaimedResume"/> has the raw value.
    /// </remarks>
    [JsonPropertyName("resumed"), JsonInclude]
    public bool? Resumed { get; internal set; }

    /// <summary>The resume claim exactly as the server sent it.</summary>
    /// <remarks>
    /// Not checked, use <see cref="Resumed"/>. Null when the server sent nothing readable.
    /// </remarks>
    [JsonIgnore]
    public bool? ServerClaimedResume { get; internal set; }

    /// <summary>How many connect attempts it took to get ready, 1 when the first one worked.</summary>
    /// <remarks>
    /// Counted since the node was last ready, so after a drop it counts the reconnects. A failed attempt raises
    /// no event, this is where it shows up.
    /// </remarks>
    [JsonIgnore]
    public int Attempts { get; internal set; }

    /// <summary>The node session id, not a Discord voice session id.</summary>
    /// <remarks>
    /// Never null. <c>Node.SessionId</c> is the id the node actually uses.
    /// </remarks>
    [JsonPropertyName("sessionId"), JsonInclude]
    public string SessionId
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;
}
