using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Places the audio at a point in space around the listener, NodeLink only.</summary>
/// <remarks>
/// NodeLink ignores the three position members, they only round-trip through the API. An instance with every
/// member null runs inert, the depth reads as 0, send explicit values to pin the sound you intend.
/// </remarks>
/// <param name="X">Left-right position, -1.0 to 1.0. NodeLink ignores it, the server just echoes it back.</param>
/// <param name="Y">Front-back position, -1.0 to 1.0. NodeLink ignores it, the server just echoes it back.</param>
/// <param name="Z">Height position, -1.0 to 1.0. NodeLink ignores it, the server just echoes it back.</param>
/// <param name="Depth">
/// Distance feel, 0.0-1.0. 0.0 leaves the filter inactive, and so does null (sent as 0).
/// </param>
/// <param name="Rate">Rotation rate in Hz.</param>
/// <param name="Transition">
/// Fades the filter in instead of stepping, and so do removals, no removal in this library carries a
/// transition, a zero depth cuts at once, the transition only fades the blend in. Parameter changes still
/// step.
/// </param>
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
