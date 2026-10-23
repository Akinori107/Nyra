using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Degrades the audio like a record player.</summary>
/// <remarks>
/// NodeLink only. An instance with every member null is not a no-op, the server applies its own defaults, an
/// audible vinyl degradation. Send explicit values to pin the sound you intend and remove the filter to turn
/// the effect off. Explicit zeros still run the static EQ (a telephone-grade curve), only the modulated
/// stages go quiet.
/// </remarks>
/// <param name="Frequency">Pitch-modulation (wow) frequency in Hz. The only member NodeLink does not clamp.</param>
/// <param name="Depth">Pitch-modulation intensity, 0.0-1.0.</param>
/// <param name="Crackle">Surface noise amount, 0.0-1.0.</param>
/// <param name="Flutter">Rapid pitch jitter, 0.0-1.0.</param>
/// <param name="Room">Room ambience simulation, 0.0-1.0.</param>
/// <param name="MicAgc">Microphone gain-control simulation, 0.0-1.0.</param>
/// <param name="Drive">Mechanical saturation, 0.0-1.0.</param>
/// <param name="Transition">
/// NodeLink currently accepts but ignores this transition, the change always snaps instantly.
/// </param>
public sealed record PhonographFilter(
    [property: JsonPropertyName("frequency")] float? Frequency = null,
    [property: JsonPropertyName("depth")] float? Depth = null,
    [property: JsonPropertyName("crackle")] float? Crackle = null,
    [property: JsonPropertyName("flutter")] float? Flutter = null,
    [property: JsonPropertyName("room")] float? Room = null,
    [property: JsonPropertyName("micAgc")] float? MicAgc = null,
    [property: JsonPropertyName("drive")] float? Drive = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter, IFiniteCheck, IStripTransition<PhonographFilter>
{
    bool IFiniteCheck.HasNonFiniteNumber =>
        FilterNumbers.NonFinite(Frequency)
        || FilterNumbers.NonFinite(Depth)
        || FilterNumbers.NonFinite(Crackle)
        || FilterNumbers.NonFinite(Flutter)
        || FilterNumbers.NonFinite(Room)
        || FilterNumbers.NonFinite(MicAgc)
        || FilterNumbers.NonFinite(Drive);

    PhonographFilter IStripTransition<PhonographFilter>.WithoutTransition() =>
        Transition is null ? this : this with { Transition = null };
}
