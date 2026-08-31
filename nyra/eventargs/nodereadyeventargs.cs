using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

public sealed class NodeReadyEventArgs : System.EventArgs
{
    /// <summary>Whether a previous session was resumed.</summary>
    [JsonPropertyName("resumed")]
    public bool Resumed { get; init; }

    /// <summary>The node session id; not a Discord voice session id.</summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; init; } = string.Empty;
}
