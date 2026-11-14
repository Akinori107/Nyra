using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Squashes the dynamic range of the audio.</summary>
/// <remarks>
/// NodeLink only. With every member null the server defaults apply, so the effect is audible. Set explicit
/// values to get the sound you want.
/// </remarks>
/// <param name="Threshold">Level above which compression kicks in, in dB, null uses the server default.</param>
/// <param name="Ratio">
/// How much the level above the threshold is reduced, 1.0 or more, null uses the server default.
/// </param>
/// <param name="Attack">
/// How fast the compressor engages, in seconds, null uses the server default. Negative values are rejected.
/// </param>
/// <param name="Release">
/// How fast the compressor lets go, in seconds, null uses the server default. Negative values are rejected.
/// </param>
/// <param name="Gain">Input gain in dB, null is sent as 0. NodeLink ignores it.</param>
/// <param name="MakeupGain">Output gain in dB, null is sent as 0.</param>
/// <param name="Transition">Fades the change instead of switching at once, NodeLink only.</param>
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
