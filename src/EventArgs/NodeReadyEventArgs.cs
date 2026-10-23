using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>A node finished its ready handshake.</summary>
public sealed class NodeReadyEventArgs : System.EventArgs
{
    /// <summary>Whether the node is running a resumed session.</summary>
    /// <remarks>
    /// The server's claim confirmed against the session id the connection replayed. A claim the cross-check
    /// could not confirm reads as false here, the safe direction. A missing claim reads as null on this
    /// member, but the raised event's <see cref="Resumed"/> is always a decided value because the node applies
    /// the verdict before raising.
    /// <para>
    /// The raw claim as sent travels in <see cref="ServerClaimedResume"/>, callers hardening against a
    /// server that sends none read <c>?? false</c>.
    /// </para>
    /// </remarks>
    [JsonPropertyName("resumed"), JsonInclude]
    [JsonConverter(typeof(NullableTolerantBooleanConverter))]
    public bool? Resumed { get; internal set; }

    /// <summary>The resume claim exactly as the server sent it.</summary>
    /// <remarks>
    /// Not a verified fact, a lying server can claim a resume it did not grant, the node's own verdict is
    /// <see cref="Resumed"/>. Null when the frame carried no readable value, a numeric one or zero and the
    /// strings "true"/"false" also read, anything else reads as null.
    /// </remarks>
    [JsonIgnore]
    public bool? ServerClaimedResume { get; internal set; }

    /// <summary>The node session id, not a Discord voice session id.</summary>
    /// <remarks>
    /// Real servers always send a string. A server that sends <c>null</c> or a non-string token
    /// overwrites the <see cref="string.Empty"/> default with null despite the annotation.
    /// <para>
    /// The getter absorbs it and never hands out null, so an absent or nulled id reads as the empty string. The
    /// raised event carries the sanitized id the node can actually use (the node drops one no HTTP layer would
    /// accept), and <c>Node.SessionId</c> is the source of truth for what the session really is.
    /// </para>
    /// </remarks>
    [JsonPropertyName("sessionId"), JsonInclude]
    [JsonConverter(typeof(NullableTolerantStringConverter))]
    public string SessionId
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;
}
