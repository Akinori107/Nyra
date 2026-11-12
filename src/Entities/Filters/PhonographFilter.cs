using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Degrades the audio like a record player.</summary>
/// <remarks>
/// NodeLink only. With every member null the server defaults apply, so the effect is audible. Set explicit
/// values to get the sound you want, and remove the filter to turn it off, since zeros still leave a
/// telephone-like EQ.
/// </remarks>
/// <param name="Frequency">Pitch-modulation (wow) frequency in Hz.</param>
/// <param name="Depth">Pitch-modulation intensity, 0.0-1.0.</param>
/// <param name="Crackle">Surface noise amount, 0.0-1.0.</param>
/// <param name="Flutter">Rapid pitch jitter, 0.0-1.0.</param>
/// <param name="Room">Room ambience simulation, 0.0-1.0.</param>
/// <param name="MicAgc">Microphone gain-control simulation, 0.0-1.0.</param>
/// <param name="Drive">Mechanical saturation, 0.0-1.0.</param>
/// <param name="Transition">Fades the change instead of switching at once, NodeLink only.</param>
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
