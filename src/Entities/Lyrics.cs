using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Outcome of a lyrics load.</summary>
public enum LyricsLoadType
{
    /// <summary>Lyrics were found.</summary>
    [JsonPropertyName("lyrics")]
    Lyrics,

    /// <summary>No provider had lyrics for the track.</summary>
    [JsonPropertyName("empty")]
    Empty,

    /// <summary>Every provider failed.</summary>
    [JsonPropertyName("error")]
    Error,
}

/// <summary>One timed line of a lyrics result; NodeLink's load response shape.</summary>
public sealed record LyricsLine
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    /// <summary>Line start, in milliseconds.</summary>
    [JsonPropertyName("time")]
    public long Time { get; init; }

    /// <summary>Line duration, in milliseconds.</summary>
    [JsonPropertyName("duration")]
    public long Duration { get; init; }

    /// <summary>Per-word timings, when the provider supplies them.</summary>
    [JsonPropertyName("words")]
    public JsonElement? Words { get; init; }
}

/// <summary>Lyrics of a track, as resolved by the node's providers.</summary>
public sealed record LyricsData
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Whether the lines carry timing information.</summary>
    [JsonPropertyName("synced")]
    public bool? Synced { get; init; }

    [JsonPropertyName("lines")]
    public IReadOnlyList<LyricsLine>? Lines { get; init; }

    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    /// <summary>Plain lyrics text, when there are no timed lines; filled by LavaLyrics.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }
}

/// <summary>Result of a lyrics load; the data shape depends on <see cref="LoadType"/>.</summary>
public sealed record LyricsResult
{
    [JsonPropertyName("loadType")]
    public LyricsLoadType LoadType { get; init; }

    /// <summary>The lyrics; null unless <see cref="LyricsLoadType.Lyrics"/>.</summary>
    [JsonPropertyName("data")]
    public LyricsData? Data { get; init; }
}
