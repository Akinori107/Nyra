using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>Raised when the player's volume changed.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed class VolumeChangedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>
    /// New volume, 0-1000, where 100 is the default.
    /// </summary>
    /// <remarks>
    /// A fractional volume is rounded.
    /// </remarks>
    [JsonPropertyName("volume"), JsonInclude]
    [JsonConverter(typeof(RoundedInt32Converter))]
    [JsonRequired]
    public int Volume { get; internal set; }
}
