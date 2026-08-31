using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>Raised when the Discord voice websocket of the player was closed.</summary>
public sealed class WebsocketClosedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    /// <summary>The Discord voice websocket close code.</summary>
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    /// <summary>Whether the websocket was closed by Discord.</summary>
    [JsonPropertyName("byRemote")]
    public bool ByRemote { get; init; }
}
