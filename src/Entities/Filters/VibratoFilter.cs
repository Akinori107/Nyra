using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Quickly oscillates the pitch.</summary>
/// <remarks>
/// Defaults on Lavalink, frequency 2.0, depth 0.5.
/// <para>
/// NodeLink reads a missing member as 0 and runs the filter inert, a partial object from a co-client stays
/// inactive there, and a full-chain write by this side fills the unset members with the defaults above,
/// activating it. Send explicit values to pin the sound you intend.
/// </para>
/// </remarks>
/// <param name="Frequency">Hz, greater than 0, up to 14.0 on Lavalink (NodeLink imposes no ceiling).</param>
/// <param name="Depth">Greater than 0, up to 1.0 on Lavalink, NodeLink clamps larger values.</param>
/// <param name="Transition">
/// Fades the filter in or out instead of stepping, NodeLink only. Parameter changes still step, only the
/// equalizer, timescale, channel mix, low-pass and high-pass animate parameter changes.
/// </param>
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
