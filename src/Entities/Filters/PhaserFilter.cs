using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Sweeps a notch through the spectrum for a whooshing effect.</summary>
/// <remarks>NodeLink only.</remarks>
/// <param name="Rate">
/// Sweep rate in Hz. 0.0 leaves the filter inactive and is rejected, and so is the null default (sent as 0).
/// </param>
/// <param name="Depth">Sweep depth, 0.0-1.0. 0.0 is rejected, null is sent as the active 1.0.</param>
/// <param name="Feedback">
/// How much of the output is fed back in, 0.0-1.0. The server clamps out-of-range values and echoes the request
/// back unclamped, null is sent as 0.
/// </param>
/// <param name="DelayMilliseconds">
/// Base delay of the pass. NodeLink ignores it, the server just echoes it back.
/// </param>
/// <param name="Mix">Effect loudness, 0.0-1.0. 0.0 is rejected, null is sent as the active 0.5.</param>
/// <param name="Stages">
/// Number of all-pass stages. The server clamps out-of-range values and treats 0 as its default. Fractional
/// values are rounded on read, NodeLink echoes the raw request back unrounded, null is sent as 4.
/// </param>
/// <param name="MinFrequency">
/// Lower bound of the pass, in Hz. Zero falls back to the server default, null is sent as 100.
/// </param>
/// <param name="MaxFrequency">
/// Upper bound of the pass, in Hz. Zero falls back to the server default, null is sent as 2500.
/// </param>
/// <param name="Transition">Fades the filter in or out instead of stepping. Parameter changes still step.</param>
public sealed record PhaserFilter(
    [property: JsonPropertyName("rate")] float? Rate = null,
    [property: JsonPropertyName("depth")] float? Depth = null,
    [property: JsonPropertyName("feedback")] float? Feedback = null,
    [property: JsonPropertyName("delay")] float? DelayMilliseconds = null,
    [property: JsonPropertyName("mix")] float? Mix = null,
    [property: JsonPropertyName("stages")]
    [property: JsonConverter(typeof(RoundedInt32Converter))]
        int? Stages = null,
    [property: JsonPropertyName("minFrequency")] float? MinFrequency = null,
    [property: JsonPropertyName("maxFrequency")] float? MaxFrequency = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<PhaserFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(Rate)
        || FilterNumbers.NonFinite(Depth)
        || FilterNumbers.NonFinite(Feedback)
        || FilterNumbers.NonFinite(DelayMilliseconds)
        || FilterNumbers.NonFinite(Mix)
        || FilterNumbers.NonFinite(MinFrequency)
        || FilterNumbers.NonFinite(MaxFrequency);

    PhaserFilter IStripTransition<PhaserFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
