using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Suppresses lower frequencies, the counterpart of the low-pass filter.</summary>
/// <remarks>NodeLink only.</remarks>
/// <param name="Smoothing">
/// Smoothing factor, 0 to 1.0 leaves the filter off and higher cuts more. Negative values are rejected.
/// </param>
/// <param name="TargetAlpha">
/// Direct coefficient override. NodeLink ignores it.
/// </param>
/// <param name="Transition">Fades the change instead of switching at once, NodeLink only.</param>
public sealed record HighPassFilter(
    [property: JsonPropertyName("smoothing")] float? Smoothing = null,
    [property: JsonPropertyName("targetAlpha")] float? TargetAlpha = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<HighPassFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(Smoothing) || FilterNumbers.NonFinite(TargetAlpha);

    HighPassFilter IStripTransition<HighPassFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
