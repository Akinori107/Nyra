using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Squashes the dynamic range of the audio. NodeLink only.</summary>
/// <param name="Threshold">Level above which compression kicks in, in dB; the server default is -24.</param>
/// <param name="Ratio">How much the level above the threshold is reduced, 1.0 or more; the server default is 4.</param>
/// <param name="Attack">How fast the compressor engages, in seconds; the server default is 0.01.</param>
/// <param name="Release">How fast the compressor lets go, in seconds; the server default is 0.1.</param>
/// <param name="Gain">Input gain in dB.</param>
/// <param name="MakeupGain">Output gain in dB.</param>
/// <param name="Transition">Animates the change instead of stepping it.</param>
public sealed record CompressorFilter(
    [property: JsonPropertyName("threshold")] float? Threshold = null,
    [property: JsonPropertyName("ratio")] float? Ratio = null,
    [property: JsonPropertyName("attack")] float? Attack = null,
    [property: JsonPropertyName("release")] float? Release = null,
    [property: JsonPropertyName("gain")] float? Gain = null,
    [property: JsonPropertyName("makeupGain")] float? MakeupGain = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter;
