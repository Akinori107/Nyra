using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Animates a filter's parameters towards their new values instead of stepping.</summary>
/// <param name="DurationMilliseconds">How long the transition takes.</param>
/// <param name="Curve">Transition curve by name, e.g. <c>linear</c> or <c>sinusoidal</c>.</param>
public sealed record FilterTransition(
    [property: JsonPropertyName("durationMs")] int DurationMilliseconds,
    [property: JsonPropertyName("curve")] string? Curve = null
);
