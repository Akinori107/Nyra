using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Crossfade fade curve, the server defaults to <see cref="Sinusoidal"/>.</summary>
public enum CrossfadeCurve
{
    /// <summary>Fades linearly over the crossfade duration.</summary>
    [JsonPropertyName("linear")]
    Linear,

    /// <summary>Fades along a sine curve.</summary>
    [JsonPropertyName("sine")]
    Sine,

    /// <summary>Fades along a sinusoidal curve.</summary>
    [JsonPropertyName("sinusoidal")]
    Sinusoidal,
}

/// <summary>How the server prepares the crossfade, the default is <see cref="Preload"/>.</summary>
public enum CrossfadeMode
{
    /// <summary>Buffers the tail of the current track and the head of the next one, then overlaps them.</summary>
    [JsonPropertyName("preload")]
    Preload,

    /// <summary>Crossfades live streams without buffered overlap.</summary>
    [JsonPropertyName("stream")]
    Stream,
}

/// <summary>
/// Per-player crossfade configuration, applied with <see cref="GuildConnection.SetCrossfadeAsync"/>.
/// Null members fall back to the server defaults (5 s sinusoidal preload with a 250 ms minimum buffer).
/// NodeLink 3.9+.
/// </summary>
public sealed record CrossfadeOptions
{
    /// <summary>Whether the ends of consecutive tracks overlap into a crossfade.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>Crossfade duration in milliseconds, the server clamps it to 0-30000.</summary>
    [JsonPropertyName("duration")]
    public int? DurationMilliseconds { get; init; }

    [JsonPropertyName("curve")]
    public CrossfadeCurve? Curve { get; init; }

    [JsonPropertyName("mode")]
    public CrossfadeMode? Mode { get; init; }

    /// <summary>Minimum PCM the server buffers before a crossfade may start, in milliseconds, 20-30000.</summary>
    [JsonPropertyName("minBufferMs")]
    public int? MinimumBufferMilliseconds { get; init; }

    /// <summary>Extra crossfade PCM buffer, in milliseconds, 0-30000.</summary>
    [JsonPropertyName("bufferMs")]
    public int? BufferMilliseconds { get; init; }
}
