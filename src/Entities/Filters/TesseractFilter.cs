using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Rotates the audio through four dimensions, an evolution of the rotation filter, NodeLink only.</summary>
/// <param name="RotationHz">
/// Rotation speed in Hz, 0.0 turns it off.
/// </param>
/// <param name="Transition">Fades the change instead of switching at once, NodeLink only.</param>
public sealed record TesseractFilter(
    [property: JsonPropertyName("rotationHz")] double? RotationHz = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<TesseractFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber => FilterNumbers.NonFinite(RotationHz);

    TesseractFilter IStripTransition<TesseractFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
