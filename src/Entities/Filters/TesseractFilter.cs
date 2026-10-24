using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Rotates the audio through four dimensions, an evolution of the rotation filter, NodeLink only.</summary>
/// <param name="RotationHz">
/// Rotation speed in Hz, NodeLink leaves anything at or below 0.001 off (0.0 is the conventional disable),
/// a value in the 0.0 to 0.001 band is rejected client-side with a named error.
/// </param>
/// <param name="Transition">
/// NodeLink currently accepts but ignores this transition, the change always snaps instantly.
/// </param>
public sealed record TesseractFilter(
    [property: JsonPropertyName("rotationHz")] double? RotationHz = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<TesseractFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber => FilterNumbers.NonFinite(RotationHz);

    TesseractFilter IStripTransition<TesseractFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
