using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Simulates a room's reflections.</summary>
/// <remarks>
/// NodeLink only. An instance with every member null is not a no-op, the mix defaults to 0.5, an audible
/// reverb, so send explicit values to pin the sound you intend.
/// <para>
/// A co-client's custom delays and gains state survives every write of this record. RoomSize and Damping only
/// reach the server while the custom state is absent, remove and re-add the filter to clear it.
/// </para>
/// </remarks>
/// <param name="RoomSize">Room size, 0.0-1.0, null is sent as 0.5.</param>
/// <param name="Damping">How much the room absorbs, 0.0-1.0, null is sent as 0.5.</param>
/// <param name="WetLevel">
/// Reverb loudness, 0.0-1.0. NodeLink ignores it (the wet level comes from the mix) and just echoes it back,
/// null is sent as 0.5.
/// </param>
/// <param name="DryLevel">
/// Original-signal loudness, 0.0-1.0. NodeLink ignores it (the dry level comes from the mix) and just echoes it
/// back, null is sent as 0.5.
/// </param>
/// <param name="Width">Stereo width of the reverb, 0.0-1.0, null is sent as 1.0.</param>
/// <param name="Mix">
/// Overall reverb mix, 0.0-1.0. 0.0 leaves the block inactive, null is sent as the audible 0.5 server default.
/// </param>
/// <param name="Transition">
/// NodeLink currently accepts but ignores this transition, the change always snaps instantly.
/// </param>
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
