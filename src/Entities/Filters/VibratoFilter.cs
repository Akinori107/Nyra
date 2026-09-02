using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Quickly oscillates the pitch. All members optional; server defaults: frequency 2.0, depth 0.5.</summary>
/// <param name="Frequency">Hz, up to 14.0.</param>
/// <param name="Depth">0.0-1.0.</param>
public sealed record VibratoFilter(
    [property: JsonPropertyName("frequency")] float? Frequency = null,
    [property: JsonPropertyName("depth")] float? Depth = null
) : IFilter;
