using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Crossfade fade curve, the server defaults to <see cref="Sinusoidal"/>.</summary>
public enum CrossfadeCurve
{
    /// <summary>Fades at a constant rate.</summary>
    Linear,

    /// <summary>Exponential curve.</summary>
    /// <remarks>
    /// NodeLink only honors the linear, sine and sinusoidal curves, the server replaces any other curve with
    /// sinusoidal or rejects the payload with 400.
    /// </remarks>
    Exponential,

    /// <summary>Logarithmic curve.</summary>
    /// <remarks>
    /// NodeLink only honors the linear, sine and sinusoidal curves, the server replaces any other curve with
    /// sinusoidal or rejects the payload with 400.
    /// </remarks>
    Logarithmic,

    /// <summary>S-curve.</summary>
    /// <remarks>Not honored by NodeLink, see <see cref="Exponential"/>.</remarks>
    SCurve,

    /// <summary>Fades along a sine curve.</summary>
    Sine,

    /// <summary>Fades along a sinusoidal curve.</summary>
    Sinusoidal,

    /// <summary>A curve the library does not know, reported by a newer server.</summary>
    /// <remarks>
    /// Sent back, the server replaces it with sinusoidal or rejects it with 400.
    /// </remarks>
    Unknown,
}

/// <summary>How the server prepares the crossfade, the default is <see cref="Preload"/>.</summary>
public enum CrossfadeMode
{
    /// <summary>
    /// Pre-buffers the head of the next track and overlaps it with the live tail of the current one.
    /// </summary>
    Preload,

    /// <summary>
    /// Accepted and echoed, but the node never arms a crossfade when either track is a live stream and always
    /// buffers the overlap, it only lowers the buffer retention default.
    /// </summary>
    /// <remarks>Do not rely on it for live-stream crossfades.</remarks>
    Stream,

    /// <summary>Lets the node pick the preparation strategy.</summary>
    /// <remarks>
    /// Not accepted by NodeLink's config gate or PATCH validation, an unmodified server silently
    /// applies the <see cref="Preload"/> profile, do not expect a behavior different from it.
    /// </remarks>
    Smart,

    /// <summary>A mode the library does not know, reported by a newer server.</summary>
    /// <remarks>
    /// Sent back, the server replaces it with preload or rejects it with 400.
    /// </remarks>
    Unknown,
}

/// <summary>
/// Per-player crossfade configuration, applied with <see cref="GuildEffects.SetCrossfadeAsync"/>.
/// </summary>
/// <remarks>
/// Null members fall back to the server defaults (5 s sinusoidal preload, 250 ms minimum buffer), except that
/// a null Enabled leaves the crossfade off, so set it explicitly. NodeLink only.
/// <para>
/// The null-duration default is the 5 s only on a node whose runtime crossfade holds a duration. A node
/// configured through its own config file echoes that partial configuration, where an enabled crossfade with a
/// null duration is left disabled at runtime.
/// </para>
/// </remarks>
public sealed record CrossfadeOptions
{
    /// <summary>Whether the ends of consecutive tracks overlap into a crossfade.</summary>
    /// <remarks>Left null the crossfade stays off, set it explicitly.</remarks>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>
    /// Crossfade duration in milliseconds, the server clamps it to 0-30000, 0 leaves the crossfade disabled at
    /// runtime even when enabled.
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
    /// Minimum PCM the server buffers from the next track before a crossfade may start, in milliseconds,
    /// 20-30000, the activation threshold saturates at 2 s, so this mainly sets the stream-mode retention
    /// floor.
    /// </summary>
    [JsonPropertyName("minBufferMs")]
    [JsonConverter(typeof(RoundedInt32Converter))]
    public int? MinimumBufferMilliseconds { get; init; }

    /// <summary>
    /// Extra crossfade PCM buffer, in milliseconds, 0-30000, the server already retains twice the duration
    /// plus 4 s (capped at 16 s), so lower values are no-ops.
    /// </summary>
    [JsonPropertyName("bufferMs")]
    [JsonConverter(typeof(RoundedInt32Converter))]
    public int? BufferMilliseconds { get; init; }
}
