using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The EternalBox engine moved to another beat of the playing track.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed class EternalBoxJumpedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The track that jumped, null when stopped.</summary>
    [JsonPropertyName("track"), JsonInclude]
    public Track? Track { get; internal set; }

    /// <summary>The jump the track took, null when the server sent none.</summary>
    [JsonPropertyName("eternalbox"), JsonInclude]
    public EternalBoxJump? Jump { get; internal set; }
}
