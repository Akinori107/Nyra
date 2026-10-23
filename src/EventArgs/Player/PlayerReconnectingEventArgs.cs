using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The voice connection of the guild's player dropped and is being re-established.</summary>
/// <remarks>
/// NodeLink only. Raised while the voice connection is being (re-)established, including a first
/// establishment that never succeeded, one raise per retry cycle. Receiving this triggers Nyra to re-send
/// a remembered failed voice forward, its failure surfaces as NodeError.
/// </remarks>
public sealed class PlayerReconnectingEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The voice state the player reconnects with, null when the server sent none.</summary>
    [JsonPropertyName("voice"), JsonInclude]
    public PlayerVoiceState? Voice { get; internal set; }
}
