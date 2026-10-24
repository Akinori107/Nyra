using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Distorts the audio.</summary>
/// <remarks>
/// The server defaults are offsets 0.0 and scales 1.0. A full-chain write by this side fills the unset members
/// with them, which activates the filter on Lavalink. NodeLink keeps the block in the applied state but
/// bypasses the default values, so the same write is inaudible there (the fade-out idiom when a transition
/// rides it). Send explicit values to pin the sound you intend, removing the filter disables it.
/// <para>
/// Lavalink multiplies the three trigonometric terms (a zeroed scale mutes the whole filter), NodeLink adds
/// them as separate terms with the offsets as phase, the same values sound different per server.
/// </para>
/// <para>
/// The NodeLink-only transition fades the filter in or out, the out leg is the default-values block carrying
/// a transition, removals themselves step, no removal in this library carries a transition. Parameter
/// changes still step.
/// </para>
/// </remarks>
public sealed record DistortionFilter(
    [property: JsonPropertyName("sinOffset")] float? SinOffset = null,
    [property: JsonPropertyName("sinScale")] float? SinScale = null,
    [property: JsonPropertyName("cosOffset")] float? CosOffset = null,
    [property: JsonPropertyName("cosScale")] float? CosScale = null,
    [property: JsonPropertyName("tanOffset")] float? TanOffset = null,
    [property: JsonPropertyName("tanScale")] float? TanScale = null,
    [property: JsonPropertyName("offset")] float? Offset = null,
    [property: JsonPropertyName("scale")] float? Scale = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<DistortionFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(SinOffset)
        || FilterNumbers.NonFinite(SinScale)
        || FilterNumbers.NonFinite(CosOffset)
        || FilterNumbers.NonFinite(CosScale)
        || FilterNumbers.NonFinite(TanOffset)
        || FilterNumbers.NonFinite(TanScale)
        || FilterNumbers.NonFinite(Offset)
        || FilterNumbers.NonFinite(Scale);

    DistortionFilter IStripTransition<DistortionFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
