using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Eliminates part of a band, usually vocals. All members optional; server defaults: level 1.0, monoLevel 1.0, filterBand 220.0, filterWidth 100.0.</summary>
/// <param name="Level">0.0-1.0; 0.0 disables the filter.</param>
/// <param name="MonoLevel">0.0-1.0.</param>
/// <param name="FilterBand">Band, in Hz.</param>
/// <param name="FilterWidth">Width, in Hz.</param>
public sealed record KaraokeFilter(
    [property: JsonPropertyName("level")] float? Level = null,
    [property: JsonPropertyName("monoLevel")] float? MonoLevel = null,
    [property: JsonPropertyName("filterBand")] float? FilterBand = null,
    [property: JsonPropertyName("filterWidth")] float? FilterWidth = null
) : IFilter;
