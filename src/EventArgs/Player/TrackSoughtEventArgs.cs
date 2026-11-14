using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>Raised after the player seeked.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed class TrackSoughtEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>Playback position after the seek, in milliseconds.</summary>
    /// <remarks>
    /// A missing value reads as 0. A value outside the <see cref="TimeSpan"/> range stays raw here and
    /// <see cref="Position"/> reads zero for it.
    /// </remarks>
    [JsonPropertyName("position"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    [JsonRequired]
    public long PositionMilliseconds { get; internal set; }

    /// <summary>Signed distance the position moved by, in milliseconds, negative for a seek backwards.</summary>
    /// <remarks>
    /// Null when the server sent none.
    /// </remarks>
    [JsonPropertyName("duration"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? DeltaMilliseconds { get; internal set; }

    /// <summary>The position after the seek.</summary>
    /// <remarks>
    /// <see cref="TimeSpan.Zero"/> when the value is out of range.
    /// </remarks>
    [JsonIgnore]
    public TimeSpan Position => TrackInfo.ToTimeSpan(PositionMilliseconds);

    /// <summary>How far the position moved, negative when seeking back.</summary>
    /// <remarks>
    /// <see cref="TimeSpan.Zero"/> when the value is out of range.
    /// </remarks>
    [JsonIgnore]
    public TimeSpan Delta =>
        DeltaMilliseconds
            is >= -TrackInfo.MaxTimeSpanMilliseconds
                and <= TrackInfo.MaxTimeSpanMilliseconds
            ? TimeSpan.FromMilliseconds(DeltaMilliseconds.Value)
            : TimeSpan.Zero;
}
