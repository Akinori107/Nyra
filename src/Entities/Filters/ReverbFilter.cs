using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Simulates a room's reflections.</summary>
/// <remarks>
/// NodeLink only. With every member null the mix is 0.5, so the reverb is audible. Set explicit values to get
/// the sound you want.
/// </remarks>
/// <param name="RoomSize">Room size, 0.0-1.0, null is sent as 0.5.</param>
/// <param name="Damping">How much the room absorbs, 0.0-1.0, null is sent as 0.5.</param>
/// <param name="WetLevel">
/// Reverb loudness, 0.0-1.0, null is sent as 0.5. NodeLink uses <paramref name="Mix"/> instead.
/// </param>
/// <param name="DryLevel">
/// Original-signal loudness, 0.0-1.0, null is sent as 0.5. NodeLink uses <paramref name="Mix"/> instead.
/// </param>
/// <param name="Width">Stereo width of the reverb, 0.0-1.0, null is sent as 1.0.</param>
/// <param name="Mix">
/// Overall reverb mix, 0.0-1.0. 0.0 turns the reverb off, null is sent as 0.5.
/// </param>
/// <param name="Transition">Fades the change instead of switching at once, NodeLink only.</param>
public sealed record ReverbFilter(
    [property: JsonPropertyName("roomSize")] float? RoomSize = null,
    [property: JsonPropertyName("damping")] float? Damping = null,
    [property: JsonPropertyName("wetLevel")] float? WetLevel = null,
    [property: JsonPropertyName("dryLevel")] float? DryLevel = null,
    [property: JsonPropertyName("width")] float? Width = null,
    [property: JsonPropertyName("mix")] float? Mix = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<ReverbFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(RoomSize)
        || FilterNumbers.NonFinite(Damping)
        || FilterNumbers.NonFinite(WetLevel)
        || FilterNumbers.NonFinite(DryLevel)
        || FilterNumbers.NonFinite(Width)
        || FilterNumbers.NonFinite(Mix);

    ReverbFilter IStripTransition<ReverbFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
