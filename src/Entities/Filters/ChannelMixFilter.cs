using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>
/// Mixes both audio channels, with the server defaults they stay independent, all factors at 0.5 make both
/// carry the same audio.
/// </summary>
/// <remarks>
/// Defaults, leftToLeft/rightToRight 1.0, leftToRight/rightToLeft 0.0. The NodeLink-only transition fades the
/// filter in or out, and the mix factors themselves animate towards the new values.
/// </remarks>
/// <param name="LeftToLeft">
/// How much of the left channel stays in the left output, 0.0-1.0, the default 1.0 keeps it independent.
/// </param>
/// <param name="LeftToRight">
/// How much of the left channel crosses into the right output, 0.0-1.0, the default 0.0 crosses none.
/// </param>
/// <param name="RightToLeft">
/// How much of the right channel crosses into the left output, 0.0-1.0, the default 0.0 crosses none.
/// </param>
/// <param name="RightToRight">
/// How much of the right channel stays in the right output, 0.0-1.0, the default 1.0 keeps it independent.
/// </param>
/// <param name="Transition">NodeLink only, fades the filter in or out instead of stepping it.</param>
public sealed record ChannelMixFilter(
    [property: JsonPropertyName("leftToLeft")] float? LeftToLeft = null,
    [property: JsonPropertyName("leftToRight")] float? LeftToRight = null,
    [property: JsonPropertyName("rightToLeft")] float? RightToLeft = null,
    [property: JsonPropertyName("rightToRight")] float? RightToRight = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<ChannelMixFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(LeftToLeft)
        || FilterNumbers.NonFinite(LeftToRight)
        || FilterNumbers.NonFinite(RightToLeft)
        || FilterNumbers.NonFinite(RightToRight);

    ChannelMixFilter IStripTransition<ChannelMixFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
