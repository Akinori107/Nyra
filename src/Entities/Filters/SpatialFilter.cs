using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Places the audio at a point in space around the listener. NodeLink only.</summary>
/// <param name="X">Left-right position, -1.0 to 1.0.</param>
/// <param name="Y">Front-back position, -1.0 to 1.0.</param>
/// <param name="Z">Height position, -1.0 to 1.0.</param>
/// <param name="Depth">Distance feel, 0.0-1.0.</param>
/// <param name="Rate">Rotation rate in Hz.</param>
/// <param name="Transition">Animates the change instead of stepping it.</param>
public sealed record SpatialFilter(
    [property: JsonPropertyName("x")] float? X = null,
    [property: JsonPropertyName("y")] float? Y = null,
    [property: JsonPropertyName("z")] float? Z = null,
    [property: JsonPropertyName("depth")] float? Depth = null,
    [property: JsonPropertyName("rate")] float? Rate = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter;
