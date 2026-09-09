using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>What a fade acts on, the server default is <see cref="Volume"/>.</summary>
public enum FadeType
{
    /// <summary>Fades the volume.</summary>
    [JsonPropertyName("volume")]
    Volume,

    /// <summary>Simulates a tape start/stop ramp.</summary>
    [JsonPropertyName("tape")]
    Tape,

    /// <summary>Simulates a vinyl scratch ramp.</summary>
    [JsonPropertyName("scratch")]
    Scratch,

    /// <summary>Both tape and scratch.</summary>
    [JsonPropertyName("both")]
    Both,
}

/// <summary>One fade of a <see cref="FadingOptions"/> chain.</summary>
public sealed record FadeSection
{
    /// <summary>Fade duration in milliseconds, 0 disables this fade.</summary>
    [JsonPropertyName("duration")]
    public int DurationMilliseconds { get; init; }

    /// <summary>Fade curve by name, e.g. <c>linear</c> or <c>sinusoidal</c>.</summary>
    [JsonPropertyName("curve")]
    public string? Curve { get; init; }

    [JsonPropertyName("type")]
    public FadeType? Type { get; init; }
}

/// <summary>
/// Automatically lowers the music while someone is speaking, part of <see cref="FadingOptions"/>.
/// NodeLink 3.9+.
/// </summary>
public sealed record DuckingOptions
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>How long the ducking takes, in milliseconds, the server default is 500.</summary>
    [JsonPropertyName("duration")]
    public int? DurationMilliseconds { get; init; }

    /// <summary>Volume to duck to, 0-1, the server default is 0.3.</summary>
    [JsonPropertyName("targetVolume")]
    public float? TargetVolume { get; init; }

    [JsonPropertyName("curve")]
    public string? Curve { get; init; }
}

/// <summary>
/// Fades around the player lifecycle (track start/end, stop, seek, pause, resume), applied with
/// <see cref="GuildConnection.SetFadingAsync"/>. A sent chain replaces the whole configuration:
/// sections left null fall back to no fade, same as <see cref="FilterChain"/>. NodeLink only.
/// </summary>
public sealed record FadingOptions
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("trackStart")]
    public FadeSection? TrackStart { get; init; }

    [JsonPropertyName("trackEnd")]
    public FadeSection? TrackEnd { get; init; }

    [JsonPropertyName("trackStop")]
    public FadeSection? TrackStop { get; init; }

    [JsonPropertyName("seek")]
    public FadeSection? Seek { get; init; }

    [JsonPropertyName("pause")]
    public FadeSection? Pause { get; init; }

    [JsonPropertyName("resume")]
    public FadeSection? Resume { get; init; }

    /// <summary>Speaking-detection ducking, NodeLink 3.9+.</summary>
    [JsonPropertyName("ducking")]
    public DuckingOptions? Ducking { get; init; }
}
