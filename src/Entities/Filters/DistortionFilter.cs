using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Distorts the audio. All members optional, server defaults: offsets 0.0, scales 1.0 (disabled).</summary>
public sealed record DistortionFilter(
    [property: JsonPropertyName("sinOffset")] float? SinOffset = null,
    [property: JsonPropertyName("sinScale")] float? SinScale = null,
    [property: JsonPropertyName("cosOffset")] float? CosOffset = null,
    [property: JsonPropertyName("cosScale")] float? CosScale = null,
    [property: JsonPropertyName("tanOffset")] float? TanOffset = null,
    [property: JsonPropertyName("tanScale")] float? TanScale = null,
    [property: JsonPropertyName("offset")] float? Offset = null,
    [property: JsonPropertyName("scale")] float? Scale = null
) : IFilter;
