using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Simulates a tape ramp; usually driven through the fade settings instead. NodeLink only.</summary>
/// <param name="DurationMilliseconds">Ramp duration.</param>
/// <param name="Type">Which edge of the ramp.</param>
/// <param name="Curve">Ramp curve by name, e.g. <c>linear</c>.</param>
public sealed record TapeFilter(
    [property: JsonPropertyName("duration")] float? DurationMilliseconds = null,
    [property: JsonPropertyName("type")] TapeRampType? Type = null,
    [property: JsonPropertyName("curve")] string? Curve = null
) : IFilter;

/// <summary>Which edge of the tape ramp.</summary>
public enum TapeRampType
{
    /// <summary>Ramps the tape up.</summary>
    [JsonPropertyName("start")]
    Start,

    /// <summary>Ramps the tape down.</summary>
    [JsonPropertyName("stop")]
    Stop,
}
