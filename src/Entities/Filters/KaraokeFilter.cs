using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Eliminates part of a band, usually vocals.</summary>
/// <remarks>
/// Server defaults, level 1.0, monoLevel 1.0, filterBand 220.0, filterWidth 100.0.
/// <para>
/// A partial object from a co-client activates at full strength on NodeLink, the missing members read as the
/// defaults above, and a full-chain write by this side fills them the same way. Send explicit values to pin
/// the sound you intend.
/// </para>
/// </remarks>
/// <param name="Level">
/// 0.0-1.0. At 0.0 NodeLink keeps the channel subtraction running (only the band shaping stops), Lavalink
/// turns the whole effect into a pass-through. Only level and monoLevel at 0.0 together turn it off on both
/// servers.
/// </param>
/// <param name="MonoLevel">
/// 0.0-1.0. At 0.0 Lavalink keeps the channel subtraction running with no band restore (a stronger effect, not
/// a disable). NodeLink reads it as one half of the pair that turns the filter off.
/// </param>
/// <param name="FilterBand">Band, in Hz. Non-negative. Zero reads as no band shaping on NodeLink.</param>
/// <param name="FilterWidth">Width, in Hz. Non-negative. Zero reads as no band shaping on NodeLink.</param>
/// <param name="Transition">
/// Fades the filter in or out instead of stepping, NodeLink only. Parameter changes still step. On Lavalink
/// the filter is stereo-only, a mono track passes through with the filter configured. NodeLink upmixes a
/// mono source to duplicated stereo and the subtraction cancels it, at the defaults a mono track goes
/// silent, remove the filter for mono sources.
/// </param>
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
