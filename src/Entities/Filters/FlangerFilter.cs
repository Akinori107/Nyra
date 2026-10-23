using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Sweeps a comb delay for a jet-plane effect.</summary>
/// <remarks>NodeLink only.</remarks>
/// <param name="Rate">
/// Sweep rate in Hz. 0.0 and the null default (sent as 0) are rejected before the wire, since they leave the
/// filter inactive and the server would accept and remember that.
/// </param>
/// <param name="Depth">
/// Sweep depth, 0.0-1.0. 0.0 and the null default (sent as 0) are rejected before the wire, since they leave
/// the filter inactive and the server would accept and remember that.
/// </param>
/// <param name="Feedback">
/// How much of the output is fed back in, 0.0-1.0. The server clamps out-of-range values and echoes the request
/// back unclamped.
/// </param>
/// <param name="DelayMilliseconds">
/// Base delay of the comb. NodeLink ignores it, the server just echoes it back.
/// </param>
/// <param name="Mix">
/// Effect loudness, 0.0-1.0. NodeLink ignores it and just echoes it back, null is sent as 0.5.
/// </param>
/// <param name="Transition">
/// NodeLink currently accepts but ignores this transition, the change always snaps instantly.
/// </param>
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
