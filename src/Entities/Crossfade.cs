using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Crossfade fade curve, the server defaults to <see cref="Sinusoidal"/>.</summary>
public enum CrossfadeCurve
{
    /// <summary>Fades at a constant rate.</summary>
    Linear,

    /// <summary>Exponential curve.</summary>
    /// <remarks>NodeLink does not support it and uses sinusoidal or rejects it.</remarks>
    Exponential,

    /// <summary>Logarithmic curve.</summary>
    /// <remarks>NodeLink does not support it and uses sinusoidal or rejects it.</remarks>
    Logarithmic,

    /// <summary>S-curve.</summary>
    /// <remarks>NodeLink does not support it and uses sinusoidal or rejects it.</remarks>
    SCurve,

    /// <summary>Fades along a sine curve.</summary>
    Sine,

    /// <summary>Fades along a sinusoidal curve.</summary>
    Sinusoidal,

    /// <summary>A curve the library does not know, reported by a newer server.</summary>
    /// <remarks>Sent back as is, the server uses sinusoidal or rejects it.</remarks>
    Unknown,
}

/// <summary>How the server prepares the crossfade, the default is <see cref="Preload"/>.</summary>
public enum CrossfadeMode
{
    /// <summary>
    /// Pre-buffers the head of the next track and overlaps it with the live tail of the current one.
    /// </summary>
    Preload,

    /// <summary>Meant for live streams, but live streams never crossfade, it only lowers the buffer kept.</summary>
    Stream,

    /// <summary>Lets the node pick the preparation strategy.</summary>
    /// <remarks>NodeLink runs it as <see cref="Preload"/>.</remarks>
    Smart,

    /// <summary>A mode the library does not know, reported by a newer server.</summary>
    /// <remarks>Sent back as is, the server uses preload or rejects it.</remarks>
    Unknown,
}

/// <summary>
/// Per-player crossfade configuration, applied with <see cref="GuildEffects.SetCrossfadeAsync"/>.
/// </summary>
/// <remarks>
/// NodeLink only. Null members use the server defaults, except <see cref="Enabled"/>, which leaves the
/// crossfade off, so set it.
/// </remarks>
public sealed record CrossfadeOptions
{
    /// <summary>Whether the ends of consecutive tracks overlap into a crossfade.</summary>
    /// <remarks>Left null the crossfade stays off, set it explicitly.</remarks>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>
    /// Crossfade duration in milliseconds, 0-30000. 0 keeps the crossfade off even when enabled.
    /// </summary>
    [JsonPropertyName("duration")]
    [JsonConverter(typeof(RoundedInt32Converter))]
    public int? DurationMilliseconds { get; init; }

    /// <summary>The curve the crossfade follows, null keeps the server default.</summary>
    [JsonPropertyName("curve")]
    public CrossfadeCurve? Curve { get; init; }

    /// <summary>The mode the crossfade runs in, null keeps the server default.</summary>
    [JsonPropertyName("mode")]
    public CrossfadeMode? Mode { get; init; }

    /// <summary>
    /// Minimum audio buffered from the next track before a crossfade can start, in milliseconds, 20-30000.
    /// </summary>
    [JsonPropertyName("minBufferMs")]
    [JsonConverter(typeof(RoundedInt32Converter))]
    public int? MinimumBufferMilliseconds { get; init; }

    /// <summary>
    /// Extra crossfade buffer, in milliseconds, 0-30000.
    /// </summary>
    [JsonPropertyName("bufferMs")]
    [JsonConverter(typeof(RoundedInt32Converter))]
    public int? BufferMilliseconds { get; init; }
}
