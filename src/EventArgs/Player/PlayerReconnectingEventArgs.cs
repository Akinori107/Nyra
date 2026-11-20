using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The voice connection of the guild's player dropped and is being re-established.</summary>
/// <remarks>
/// NodeLink only. Raised once per retry, also when the first connection never worked. Nyra then re-sends a
/// voice update that failed before, and a new failure goes to NodeError.
/// </remarks>
public sealed class PlayerReconnectingEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The voice state the player reconnects with, null when the server sent none.</summary>
    [JsonPropertyName("voice"), JsonInclude]
    public PlayerVoiceState? Voice { get; internal set; }
}
