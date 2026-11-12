using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Distorts the audio.</summary>
/// <remarks>
/// The server defaults are offsets 0.0 and scales 1.0, and unset members are sent as those. On Lavalink they
/// still change the sound while another filter is on, on NodeLink they do nothing. Set explicit values to get
/// the sound you want, and remove the filter to turn it off. The same values sound different on each server.
/// </remarks>
/// <param name="SinOffset">Offset of the sine term, the server default is 0.0.</param>
/// <param name="SinScale">Scale of the sine term, the server default is 1.0.</param>
/// <param name="CosOffset">Offset of the cosine term, the server default is 0.0.</param>
/// <param name="CosScale">Scale of the cosine term, the server default is 1.0.</param>
/// <param name="TanOffset">Offset of the tangent term, the server default is 0.0.</param>
/// <param name="TanScale">Scale of the tangent term, the server default is 1.0.</param>
/// <param name="Offset">General offset, the server default is 0.0.</param>
/// <param name="Scale">General scale, the server default is 1.0.</param>
/// <param name="Transition">Fades the change instead of switching at once, NodeLink only.</param>
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
