using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>
/// Chapter of a track, from NodeLink's /v4/loadchapters, only YouTube and YouTube Music tracks carry them.
/// </summary>
public sealed record Chapter
{
    /// <summary>The title of the chapter.</summary>
    /// <remarks>
    /// Absorbs a null title the server sends, the getter never hands out null, a re-serialized chapter always
    /// carries the title as a string.
    /// </remarks>
    [JsonPropertyName("title"), JsonInclude]
    public string Title
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Chapter start, in milliseconds.</summary>
    /// <remarks>
    /// No rounding converter, the server reports whole milliseconds here, a fractional start never reaches the
    /// wire.
    /// </remarks>
    [JsonPropertyName("startTime"), JsonInclude]
    public long StartTime { get; internal set; }

    /// <summary>Chapter length, in milliseconds, derived from the next chapter's start.</summary>
    /// <remarks>
    /// A zero or negative value is the server reporting two boundaries at the same instant (zero) or out of
    /// order (negative), not a length. A stream track reports the stream marker here, also not a length.
    /// </remarks>
    [JsonPropertyName("duration"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? Duration { get; internal set; }

    /// <summary>Chapter end, in milliseconds.</summary>
    /// <remarks>
    /// Redundant by construction (the start of the next chapter, or the track's own end for the last one), the
    /// server also sends it explicitly, so it is bound for completeness, treat null as unknown.
    /// </remarks>
    [JsonPropertyName("endTime"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? EndTime { get; internal set; }

    /// <summary>The thumbnails of the chapter, raw JSON.</summary>
    [JsonPropertyName("thumbnails"), JsonInclude]
    public JsonElement? Thumbnails { get; internal set; }

    /// <summary>Indicates whether the chapters carry the same title, times and thumbnails.</summary>
    /// <param name="other">The chapter to compare with.</param>
    /// <returns>Whether the members match, the raw thumbnails compared by value.</returns>
    public bool Equals(Chapter? other) =>
        other is not null
        && Title == other.Title
        && StartTime == other.StartTime
        && Duration == other.Duration
        && EndTime == other.EndTime
        && ContentEquality.JsonElementEquals(Thumbnails, other.Thumbnails);

    /// <summary>Returns a hash of the chapter's title and times, the raw thumbnails stay out of it.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Title, StartTime, Duration, EndTime);
}
