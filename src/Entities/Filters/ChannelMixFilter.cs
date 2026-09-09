using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>
/// Mixes both audio channels with a configurable factor, with the server's defaults both
/// channels stay independent, all factors at 0.5 makes both carry the same audio. All members
/// optional, server defaults: leftToLeft 1.0, rightToRight 1.0, leftToRight 0.0, rightToLeft 0.0.
/// </summary>
public sealed record ChannelMixFilter(
    [property: JsonPropertyName("leftToLeft")] float? LeftToLeft = null,
    [property: JsonPropertyName("leftToRight")] float? LeftToRight = null,
    [property: JsonPropertyName("rightToLeft")] float? RightToLeft = null,
    [property: JsonPropertyName("rightToRight")] float? RightToRight = null
) : IFilter;
