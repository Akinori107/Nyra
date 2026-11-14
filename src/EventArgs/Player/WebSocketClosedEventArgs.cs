using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>Raised when the Discord voice websocket of the player was closed.</summary>
/// <remarks>
/// On NodeLink a close here does not always come from Discord, even with a 4xxx code. Check
/// <see cref="Reason"/> before blaming Discord.
/// </remarks>
public sealed class WebSocketClosedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>Discord voice close event code, the 4xxx family signals failures.</summary>
    /// <remarks>NodeLink can send codes of its own and null when the voice connection failed without a close.</remarks>
    [JsonPropertyName("code"), JsonInclude]
    public int? Code { get; internal set; }

    /// <summary>Why the voice websocket closed.</summary>
    /// <remarks>
    /// A null reason from the server reads as an empty string.
    /// </remarks>
    [JsonPropertyName("reason"), JsonInclude]
    public string Reason
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Whether the websocket was closed by Discord.</summary>
    /// <remarks>
    /// On NodeLink it can be true for closes that did not come from Discord.
    /// </remarks>
    [JsonPropertyName("byRemote"), JsonInclude]
    public bool ByRemote { get; internal set; }
}
