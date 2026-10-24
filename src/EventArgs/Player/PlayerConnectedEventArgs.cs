using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The voice connection of the guild's player became established.</summary>
/// <remarks>
/// NodeLink only. The initial establishment races the connect itself, the frame can dispatch before any
/// handler can subscribe to the connection, the reliable signal for the first join is the connect completing
/// (play and filter commands work from that completion). Reconnect cycles raise this while subscribed.
/// </remarks>
public sealed class PlayerConnectedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The voice state the player connected with, null when the server sent none.</summary>
    [JsonPropertyName("voice"), JsonInclude]
    public PlayerVoiceState? Voice { get; internal set; }
}
