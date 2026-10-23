using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Squashes the dynamic range of the audio.</summary>
/// <remarks>
/// NodeLink only. An instance with every member null is not a no-op, the server applies its own defaults, send
/// explicit values to pin the sound you intend.
/// </remarks>
/// <param name="Threshold">Level above which compression kicks in, in dB, null uses the server default.</param>
/// <param name="Ratio">
/// How much the level above the threshold is reduced, 1.0 or more, null uses the server default. Values below
/// 1.0 are rejected client-side before the wire.
/// </param>
/// <param name="Attack">
/// How fast the compressor engages, in seconds, null uses the server default. The server clamps values below
/// its minimum. Negative values are rejected client-side before the wire.
/// </param>
/// <param name="Release">
/// How fast the compressor lets go, in seconds, null uses the server default. The server clamps values below
/// its minimum. Negative values are rejected client-side before the wire.
/// </param>
/// <param name="Gain">Input gain in dB. NodeLink ignores it and just echoes it back, null is sent as 0.</param>
/// <param name="MakeupGain">Output gain in dB, null is sent as 0.</param>
/// <param name="Transition">
/// Fades the filter in instead of stepping, the out leg has no reachable idiom for this filter, parameter
/// changes still step.
/// </param>
public sealed record CompressorFilter(
    [property: JsonPropertyName("threshold")] float? Threshold = null,
    [property: JsonPropertyName("ratio")] float? Ratio = null,
    [property: JsonPropertyName("attack")] float? Attack = null,
    [property: JsonPropertyName("release")] float? Release = null,
    [property: JsonPropertyName("gain")] float? Gain = null,
    [property: JsonPropertyName("makeupGain")] float? MakeupGain = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<CompressorFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(Threshold)
        || FilterNumbers.NonFinite(Ratio)
        || FilterNumbers.NonFinite(Attack)
        || FilterNumbers.NonFinite(Release)
        || FilterNumbers.NonFinite(Gain)
        || FilterNumbers.NonFinite(MakeupGain);

    CompressorFilter IStripTransition<CompressorFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
