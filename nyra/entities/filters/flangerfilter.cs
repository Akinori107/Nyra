using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Sweeps a comb delay for a jet-plane effect. NodeLink only.</summary>
/// <param name="Rate">Sweep rate in Hz.</param>
/// <param name="Depth">Sweep depth, 0.0-1.0.</param>
/// <param name="Feedback">How much of the output is fed back in, 0.0-1.0.</param>
/// <param name="DelayMilliseconds">Base delay of the comb.</param>
/// <param name="Mix">Effect loudness, 0.0-1.0.</param>
/// <param name="Transition">Animates the change instead of stepping it.</param>
public sealed record FlangerFilter(
    [property: JsonPropertyName("rate")] float? Rate = null,
    [property: JsonPropertyName("depth")] float? Depth = null,
    [property: JsonPropertyName("feedback")] float? Feedback = null,
    [property: JsonPropertyName("delay")] float? DelayMilliseconds = null,
    [property: JsonPropertyName("mix")] float? Mix = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter;
