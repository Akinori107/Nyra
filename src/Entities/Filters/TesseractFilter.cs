using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Rotates the audio through four dimensions, an evolution of the rotation filter. NodeLink 3.9+.</summary>
/// <param name="RotationHz">Rotation speed in Hz.</param>
/// <param name="Transition">Animates the change instead of stepping it.</param>
public sealed record TesseractFilter(
    [property: JsonPropertyName("rotationHz")] double? RotationHz = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter;
