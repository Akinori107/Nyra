using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Changes speed, pitch and rate. All members optional; server defaults: 1.0 (no change).</summary>
public sealed record TimescaleFilter(
    [property: JsonPropertyName("speed")] double? Speed = null,
    [property: JsonPropertyName("pitch")] double? Pitch = null,
    [property: JsonPropertyName("rate")] double? Rate = null
) : IFilter;
