using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Rotates the audio around the stereo channels (audio panning).</summary>
/// <param name="RotationHz">
/// Rotation frequency in Hz, 0.0 turns it off.
/// </param>
/// <param name="Transition">Fades the change instead of switching at once, NodeLink only.</param>
public sealed record RotationFilter(
    [property: JsonPropertyName("rotationHz")] double? RotationHz = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<RotationFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber => FilterNumbers.NonFinite(RotationHz);

    RotationFilter IStripTransition<RotationFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
