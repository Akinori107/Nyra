using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Suppresses lower frequencies, the counterpart of the low-pass filter. NodeLink only.</summary>
/// <param name="Smoothing">Smoothing factor; the server default is 20.</param>
/// <param name="TargetAlpha">Direct smoothing coefficient override, 0.0-1.0.</param>
/// <param name="Transition">Animates the change instead of stepping it.</param>
public sealed record HighPassFilter(
    [property: JsonPropertyName("smoothing")] float? Smoothing = null,
    [property: JsonPropertyName("targetAlpha")] float? TargetAlpha = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter;
