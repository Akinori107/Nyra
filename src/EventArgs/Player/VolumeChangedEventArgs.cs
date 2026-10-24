using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>Raised when the player's volume changed.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed class VolumeChangedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>
    /// New volume, 0-1000, where 100 is the default, as reported, the server enforces the range, the read does
    /// not clamp.
    /// </summary>
    /// <remarks>
    /// NodeLink accepts fractional volumes, so the value is rounded on read. A frame without the member is
    /// rejected, not read as a volume of zero.
    /// </remarks>
    [JsonPropertyName("volume"), JsonInclude]
    [JsonConverter(typeof(RoundedInt32Converter))]
    [JsonRequired]
    public int Volume { get; internal set; }
}
