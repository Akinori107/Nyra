using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Eliminates part of a band, usually vocals.</summary>
/// <remarks>
/// Server defaults, level 1.0, monoLevel 1.0, filterBand 220.0, filterWidth 100.0.
/// Unset members are sent as these defaults, which is the full effect. Set explicit values to get the sound you
/// want. A mono track goes almost silent on both servers, so remove the filter for mono sources.
/// </remarks>
/// <param name="Level">
/// 0.0-1.0. To turn the effect off on both servers, set this and <paramref name="MonoLevel"/> to 0.0.
/// </param>
/// <param name="MonoLevel">
/// 0.0-1.0. On its own, 0.0 makes the effect stronger on Lavalink instead of turning it off.
/// </param>
/// <param name="FilterBand">Band, in Hz. Non-negative.</param>
/// <param name="FilterWidth">Width, in Hz. Non-negative.</param>
/// <param name="Transition">Fades the change instead of switching at once, NodeLink only.</param>
public sealed record KaraokeFilter(
    [property: JsonPropertyName("level")] float? Level = null,
    [property: JsonPropertyName("monoLevel")] float? MonoLevel = null,
    [property: JsonPropertyName("filterBand")] float? FilterBand = null,
    [property: JsonPropertyName("filterWidth")] float? FilterWidth = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<KaraokeFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(Level)
        || FilterNumbers.NonFinite(MonoLevel)
        || FilterNumbers.NonFinite(FilterBand)
        || FilterNumbers.NonFinite(FilterWidth);

    KaraokeFilter IStripTransition<KaraokeFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
