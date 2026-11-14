using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Sweeps a comb delay for a jet-plane effect.</summary>
/// <remarks>NodeLink only.</remarks>
/// <param name="Rate">
/// Sweep rate in Hz. 0.0 and null are rejected, since they leave the filter silent.
/// </param>
/// <param name="Depth">
/// Sweep depth, 0.0-1.0. 0.0 and null are rejected, since they leave the filter silent.
/// </param>
/// <param name="Feedback">
/// How much of the output is fed back in, 0.0-1.0.
/// </param>
/// <param name="DelayMilliseconds">Base delay of the comb. NodeLink ignores it.</param>
/// <param name="Mix">
/// Effect loudness, 0.0-1.0, null is sent as 0.5. NodeLink ignores it.
/// </param>
/// <param name="Transition">Fades the change instead of switching at once, NodeLink only.</param>
public sealed record FlangerFilter(
    [property: JsonPropertyName("rate")] float? Rate = null,
    [property: JsonPropertyName("depth")] float? Depth = null,
    [property: JsonPropertyName("feedback")] float? Feedback = null,
    [property: JsonPropertyName("delay")] float? DelayMilliseconds = null,
    [property: JsonPropertyName("mix")] float? Mix = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<FlangerFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(Rate)
        || FilterNumbers.NonFinite(Depth)
        || FilterNumbers.NonFinite(Feedback)
        || FilterNumbers.NonFinite(DelayMilliseconds)
        || FilterNumbers.NonFinite(Mix);

    FlangerFilter IStripTransition<FlangerFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
