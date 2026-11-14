using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Quickly oscillates the volume.</summary>
/// <remarks>
/// Defaults on Lavalink, frequency 2.0, depth 0.5.
/// Unset members are sent as these defaults. Set explicit values to get the sound you want.
/// </remarks>
/// <param name="Frequency">Hz, greater than 0.</param>
/// <param name="Depth">
/// Greater than 0 and up to 1.0, Lavalink rejects a larger value, NodeLink clamps it.
/// </param>
/// <param name="Transition">Fades the change instead of switching at once, NodeLink only.</param>
public sealed record TremoloFilter(
    [property: JsonPropertyName("frequency")] float? Frequency = null,
    [property: JsonPropertyName("depth")] float? Depth = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<TremoloFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(Frequency) || FilterNumbers.NonFinite(Depth);

    TremoloFilter IStripTransition<TremoloFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
