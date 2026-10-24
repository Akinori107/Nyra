using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Suppresses lower frequencies, the counterpart of the low-pass filter.</summary>
/// <remarks>NodeLink only.</remarks>
/// <param name="Smoothing">
/// Smoothing factor, the server default is 0, and any value of 0 to 1.0 leaves the filter inactive. Negative
/// values are rejected client-side before the wire. Higher smoothing cuts more audio.
/// </param>
/// <param name="TargetAlpha">
/// Direct coefficient override in the settings schema, but NodeLink ignores it (the coefficient comes from the
/// smoothing), the server just echoes it back.
/// </param>
/// <param name="Transition">
/// Fades the filter in or out instead of stepping. A smoothing change sent with a transition animates towards
/// the new coefficient too, like the low-pass counterpart.
/// </param>
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
