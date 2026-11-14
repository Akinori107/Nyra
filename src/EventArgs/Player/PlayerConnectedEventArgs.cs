using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The voice connection of the guild's player became established.</summary>
/// <remarks>
/// NodeLink only. The first connection usually fires before a handler can subscribe, so treat the end of the
/// connect as that one.
/// </remarks>
public sealed class PlayerConnectedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The voice state the player connected with, null when the server sent none.</summary>
    [JsonPropertyName("voice"), JsonInclude]
    public PlayerVoiceState? Voice { get; internal set; }
}
