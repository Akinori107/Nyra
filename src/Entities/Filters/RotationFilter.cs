using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Rotates the audio around the stereo channels (audio panning).</summary>
/// <param name="RotationHz">
/// Rotation frequency in Hz, 0.0 disables the filter. NodeLink disables anything at or below 0.001 (only
/// values above it activate), Lavalink only exact 0.0.
/// </param>
/// <param name="Transition">
/// Fades the filter in instead of stepping, NodeLink only, it fades the filter out too (RotationHz 0.0 with
/// a transition, the wet blend animates towards the disable, the rotation keeps its old rate during the
/// fade). Parameter changes still step, and so do removals, no removal in this library carries a
/// transition.
/// </param>
public sealed record RotationFilter(
    [property: JsonPropertyName("rotationHz")] double? RotationHz = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<RotationFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber => FilterNumbers.NonFinite(RotationHz);

    RotationFilter IStripTransition<RotationFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
