using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Places the audio at a point in space around the listener, NodeLink only.</summary>
/// <remarks>
/// NodeLink ignores <paramref name="X"/>, <paramref name="Y"/> and <paramref name="Z"/>. With every member null
/// the filter does nothing.
/// </remarks>
/// <param name="X">Left-right position, -1.0 to 1.0. NodeLink ignores it.</param>
/// <param name="Y">Front-back position, -1.0 to 1.0. NodeLink ignores it.</param>
/// <param name="Z">Height position, -1.0 to 1.0. NodeLink ignores it.</param>
/// <param name="Depth">
/// Distance feel, 0.0-1.0. 0.0 and null leave the filter off.
/// </param>
/// <param name="Rate">Rotation rate in Hz.</param>
/// <param name="Transition">Fades the change instead of switching at once, NodeLink only.</param>
public sealed record SpatialFilter(
    [property: JsonPropertyName("x")] float? X = null,
    [property: JsonPropertyName("y")] float? Y = null,
    [property: JsonPropertyName("z")] float? Z = null,
    [property: JsonPropertyName("depth")] float? Depth = null,
    [property: JsonPropertyName("rate")] float? Rate = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<SpatialFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(X)
        || FilterNumbers.NonFinite(Y)
        || FilterNumbers.NonFinite(Z)
        || FilterNumbers.NonFinite(Depth)
        || FilterNumbers.NonFinite(Rate);

    SpatialFilter IStripTransition<SpatialFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
