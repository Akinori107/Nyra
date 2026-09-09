using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>A SponsorBlock segment of the current track.</summary>
public sealed record SponsorBlockSegment
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; init; } = string.Empty;

    /// <summary>Segment start, in milliseconds.</summary>
    [JsonPropertyName("start")]
    public long Start { get; init; }

    /// <summary>Segment end, in milliseconds.</summary>
    [JsonPropertyName("end")]
    public long End { get; init; }

    /// <summary>SponsorBlock category, e.g. <c>sponsor</c> or <c>selfpromo</c>.</summary>
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    /// <summary>Action the server takes on the segment, e.g. <c>skip</c> or <c>mute</c>.</summary>
    [JsonPropertyName("actionType")]
    public string ActionType { get; init; } = string.Empty;

    [JsonPropertyName("votes")]
    public int Votes { get; init; }

    [JsonPropertyName("locked")]
    public bool Locked { get; init; }

    /// <summary>Length of the video the segment was submitted for, in milliseconds.</summary>
    [JsonPropertyName("videoDuration")]
    public long VideoDuration { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

/// <summary>SponsorBlock state of a player, as reported by the server.</summary>
public sealed record SponsorBlockState
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("categories")]
    public IReadOnlyList<string> Categories { get; init; } = [];

    [JsonPropertyName("actionTypes")]
    public IReadOnlyList<string> ActionTypes { get; init; } = [];

    [JsonPropertyName("segments")]
    public IReadOnlyList<SponsorBlockSegment> Segments { get; init; } = [];

    /// <summary>Uuid of the segment that was skipped last.</summary>
    [JsonPropertyName("lastSkippedUuid")]
    public string? LastSkippedUuid { get; init; }

    /// <summary>How long before a segment boundary the server skips, in milliseconds.</summary>
    [JsonPropertyName("skipMarginMs")]
    public int SkipMarginMilliseconds { get; init; }
}

/// <summary>SponsorBlock settings to change, members left null keep their current value.</summary>
public sealed record SponsorBlockSettings
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("categories")]
    public IReadOnlyList<string>? Categories { get; init; }

    [JsonPropertyName("actionTypes")]
    public IReadOnlyList<string>? ActionTypes { get; init; }

    [JsonPropertyName("skipMarginMs")]
    public int? SkipMarginMilliseconds { get; init; }
}

/// <summary>Request body of the SponsorBlock segment override endpoint.</summary>
internal sealed record SponsorBlockSegmentOverride(
    [property: JsonPropertyName("segments")] IReadOnlyList<SponsorBlockSegment> Segments
);
