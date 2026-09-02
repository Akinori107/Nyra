using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Repeats the audio after a delay. NodeLink only.</summary>
/// <param name="DelayMilliseconds">Echo delay; the server clamps it to 0-2000.</param>
/// <param name="Feedback">How much of the echo is fed back in, 0.0-1.0.</param>
/// <param name="Mix">Echo loudness, 0.0-1.0.</param>
/// <param name="Transition">Animates the change instead of stepping it.</param>
public sealed record EchoFilter(
    [property: JsonPropertyName("delay")] float? DelayMilliseconds = null,
    [property: JsonPropertyName("feedback")] float? Feedback = null,
    [property: JsonPropertyName("mix")] float? Mix = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter;
