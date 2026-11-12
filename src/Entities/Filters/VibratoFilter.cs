using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Quickly oscillates the pitch.</summary>
/// <remarks>
/// Defaults on Lavalink, frequency 2.0, depth 0.5.
/// Unset members are sent as these defaults. Set explicit values to get the sound you want.
/// </remarks>
/// <param name="Frequency">Hz, greater than 0, up to 14.0 on Lavalink.</param>
/// <param name="Depth">Greater than 0, up to 1.0 on Lavalink, NodeLink clamps larger values.</param>
/// <param name="Transition">Fades the change instead of switching at once, NodeLink only.</param>
public sealed record VibratoFilter(
    [property: JsonPropertyName("frequency")] float? Frequency = null,
    [property: JsonPropertyName("depth")] float? Depth = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<VibratoFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(Frequency) || FilterNumbers.NonFinite(Depth);

    VibratoFilter IStripTransition<VibratoFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
