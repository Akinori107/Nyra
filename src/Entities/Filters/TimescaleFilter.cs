using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Changes speed, pitch and rate.</summary>
/// <remarks>
/// Server defaults, 1.0 (no change).
/// <para>
/// The NodeLink-only transition fades the filter in or out, parameter changes animate towards the new
/// values. During the transition the position estimate advances step-wise at the target pace, it does not
/// interpolate the ramp.
/// </para>
/// </remarks>
/// <param name="Speed">Speed multiplier, greater than 0, the server default is 1.0 (no change).</param>
/// <param name="Pitch">Pitch multiplier, greater than 0, the server default is 1.0 (no change).</param>
/// <param name="Rate">Rate multiplier, greater than 0, the server default is 1.0 (no change).</param>
/// <param name="Transition">
/// Fades the filter in or out instead of stepping, NodeLink only. Parameter changes animate towards the new
/// values too.
/// </param>
public sealed record TimescaleFilter(
    [property: JsonPropertyName("speed")] double? Speed = null,
    [property: JsonPropertyName("pitch")] double? Pitch = null,
    [property: JsonPropertyName("rate")] double? Rate = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<TimescaleFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(Speed)
        || FilterNumbers.NonFinite(Pitch)
        || FilterNumbers.NonFinite(Rate);

    TimescaleFilter IStripTransition<TimescaleFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
