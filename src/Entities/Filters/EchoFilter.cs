using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Repeats the audio after a delay.</summary>
/// <remarks>
/// NodeLink only. An instance with every member null is rejected, the delay and mix both read as their
/// rejected 0.0 neutral.
/// </remarks>
/// <param name="DelayMilliseconds">
/// Echo delay in milliseconds, greater than 0 (null is sent as 0 and rejected). The server clamps values above
/// its maximum.
/// </param>
/// <param name="Feedback">How much of the echo is fed back in, 0.0-1.0.</param>
/// <param name="Mix">
/// Echo loudness, 0.0-1.0. 0.0 leaves the block inactive and is rejected, and so is null (sent as 0).
/// </param>
/// <param name="Transition">Fades the filter in or out instead of stepping. Parameter changes still step.</param>
public sealed record EchoFilter(
    [property: JsonPropertyName("delay")] float? DelayMilliseconds = null,
    [property: JsonPropertyName("feedback")] float? Feedback = null,
    [property: JsonPropertyName("mix")] float? Mix = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<EchoFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(DelayMilliseconds)
        || FilterNumbers.NonFinite(Feedback)
        || FilterNumbers.NonFinite(Mix);

    EchoFilter IStripTransition<EchoFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
