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
    /// A null title reads as an empty string.
    /// </remarks>
    [JsonPropertyName("title"), JsonInclude]
    public string Title
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Chapter start, in milliseconds.</summary>
    [JsonPropertyName("startTime"), JsonInclude]
    public long StartTime { get; internal set; }

    /// <summary>Chapter length, in milliseconds, derived from the next chapter's start.</summary>
    /// <remarks>
    /// Zero or negative when the server sent bad boundaries, and the stream marker on a stream.
    /// </remarks>
    [JsonPropertyName("duration"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? Duration { get; internal set; }

    /// <summary>Chapter end, in milliseconds.</summary>
    /// <remarks>
    /// Null when unknown.
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
