using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Rotates the audio around the stereo channels (audio panning).</summary>
/// <param name="RotationHz">Rotation frequency in Hz; 0.0 disables the filter.</param>
public sealed record RotationFilter(
    [property: JsonPropertyName("rotationHz")] double? RotationHz = null
) : IFilter;
