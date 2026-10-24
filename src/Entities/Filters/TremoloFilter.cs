using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Quickly oscillates the volume.</summary>
/// <remarks>
/// Defaults on Lavalink, frequency 2.0, depth 0.5.
/// <para>
/// NodeLink reads a missing member as 0 and runs the filter inert, a partial object from a co-client stays
/// inactive there, and a full-chain write by this side fills the unset members with the defaults above,
/// activating it. Send explicit values to pin the sound you intend.
/// </para>
/// </remarks>
/// <param name="Frequency">Hz, greater than 0.</param>
/// <param name="Depth">
/// Greater than 0 and up to 1.0, Lavalink rejects a larger value, NodeLink clamps it.
/// </param>
/// <param name="Transition">
/// Fades the filter in or out instead of stepping, NodeLink only. Parameter changes still step.
/// </param>
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
