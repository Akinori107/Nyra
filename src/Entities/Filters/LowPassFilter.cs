using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Suppresses higher frequencies. Smoothing of 1.0 or less disables the filter.</summary>
public sealed record LowPassFilter(
    [property: JsonPropertyName("smoothing")] float? Smoothing = null
) : IFilter;
