using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Layers slightly delayed copies for a thickening effect.</summary>
/// <remarks>NodeLink only.</remarks>
/// <param name="Rate">
/// Modulation rate in Hz. 0.0 leaves the filter inactive and is rejected, and so is the null default (sent as
/// 0).
/// </param>
/// <param name="Depth">
/// Modulation depth, 0.0-1.0. 0.0 leaves the filter inactive and is rejected, and so is the null default (sent
/// as 0).
/// </param>
/// <param name="Feedback">
/// How much of the output is fed back in, 0.0-1.0. The server clamps out-of-range values and echoes the request
/// back unclamped.
/// </param>
/// <param name="DelayMilliseconds">
/// Base delay of the copies, null is sent as 25. The server clamps out-of-range values and reads an explicit
/// 0.0 as the null default.
/// </param>
/// <param name="Mix">Effect loudness, 0.0-1.0. 0.0 is rejected, null is sent as the active 0.5.</param>
/// <param name="Transition">Fades the filter in or out instead of stepping. Parameter changes still step.</param>
public sealed record ChorusFilter(
    [property: JsonPropertyName("rate")] float? Rate = null,
    [property: JsonPropertyName("depth")] float? Depth = null,
    [property: JsonPropertyName("feedback")] float? Feedback = null,
    [property: JsonPropertyName("delay")] float? DelayMilliseconds = null,
    [property: JsonPropertyName("mix")] float? Mix = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<ChorusFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(Rate)
        || FilterNumbers.NonFinite(Depth)
        || FilterNumbers.NonFinite(Feedback)
        || FilterNumbers.NonFinite(DelayMilliseconds)
        || FilterNumbers.NonFinite(Mix);

    ChorusFilter IStripTransition<ChorusFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
