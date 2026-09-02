using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Quickly oscillates the volume. All members optional; server defaults: frequency 2.0, depth 0.5.</summary>
/// <param name="Frequency">Hz, greater than 0.</param>
/// <param name="Depth">0.0-1.0.</param>
public sealed record TremoloFilter(
    [property: JsonPropertyName("frequency")] float? Frequency = null,
    [property: JsonPropertyName("depth")] float? Depth = null
) : IFilter;
