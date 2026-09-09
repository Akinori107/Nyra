using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>One chapter of a track, in milliseconds.</summary>
public sealed record Chapter
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Chapter start, in milliseconds.</summary>
    [JsonPropertyName("startTime")]
    public long StartTime { get; init; }

    /// <summary>Chapter length, in milliseconds, absent on the last chapter of some sources.</summary>
    [JsonPropertyName("duration")]
    public long? Duration { get; init; }

    /// <summary>Chapter thumbnails as reported by the source.</summary>
    [JsonPropertyName("thumbnails")]
    public JsonElement? Thumbnails { get; init; }
}
