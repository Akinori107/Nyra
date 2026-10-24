using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>Raised after the player seeked.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed class TrackSoughtEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>Playback position after the seek, in milliseconds.</summary>
    /// <remarks>
    /// A null on the wire is strict, the frame is dropped whole and surfaces as a NodeError rather than read
    /// as a seek to the start, and a frame without the member reads as 0, indistinguishable from a seek to
    /// the start.
    /// <para>
    /// A value outside the <see cref="TimeSpan"/> range (e.g. a stream marker the server echoed back)
    /// survives raw here, <see cref="Position"/> reads zero for it.
    /// </para>
    /// </remarks>
    [JsonPropertyName("position"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    [JsonRequired]
    public long PositionMilliseconds { get; internal set; }

    /// <summary>Signed distance the position moved by, in milliseconds, negative for a seek backwards.</summary>
    /// <remarks>
    /// Carried on the wire as <c>duration</c>. Null when the frame carried none, it reads as no movement, a
    /// missing delta must not take the frame's valid position down with it, a value that is not a number at
    /// all drops the event whole.
    /// </remarks>
    [JsonPropertyName("duration"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? DeltaMilliseconds { get; internal set; }

    /// <summary>The position after the seek.</summary>
    /// <remarks>
    /// Reads <see cref="TimeSpan.Zero"/> when the value is outside the <see cref="TimeSpan"/> range, e.g. a
    /// stream marker the server echoed back.
    /// </remarks>
    [JsonIgnore]
    public TimeSpan Position => TrackInfo.ToTimeSpan(PositionMilliseconds);

    /// <summary>How far the position moved, negative when seeking back.</summary>
    /// <remarks>
    /// Reads <see cref="TimeSpan.Zero"/> when the value is outside the <see cref="TimeSpan"/> range, e.g. a
    /// stream marker the server echoed back.
    /// </remarks>
    [JsonIgnore]
    public TimeSpan Delta =>
        DeltaMilliseconds
            is >= -TrackInfo.MaxTimeSpanMilliseconds
                and <= TrackInfo.MaxTimeSpanMilliseconds
            ? TimeSpan.FromMilliseconds(DeltaMilliseconds.Value)
            : TimeSpan.Zero;
}
