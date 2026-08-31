using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Degrades the audio like a record player. NodeLink only.</summary>
/// <param name="Frequency">Pitch-modulation (wow) frequency in Hz.</param>
/// <param name="Depth">Pitch-modulation intensity, 0.0-1.0.</param>
/// <param name="Crackle">Surface noise amount, 0.0-1.0.</param>
/// <param name="Flutter">Rapid pitch jitter, 0.0-1.0.</param>
/// <param name="Room">Room ambience simulation, 0.0-1.0.</param>
/// <param name="MicAgc">Microphone gain-control simulation, 0.0-1.0.</param>
/// <param name="Drive">Mechanical saturation, 0.0-1.0.</param>
/// <param name="Transition">Animates the change instead of stepping it.</param>
public sealed record PhonographFilter(
    [property: JsonPropertyName("frequency")] float? Frequency = null,
    [property: JsonPropertyName("depth")] float? Depth = null,
    [property: JsonPropertyName("crackle")] float? Crackle = null,
    [property: JsonPropertyName("flutter")] float? Flutter = null,
    [property: JsonPropertyName("room")] float? Room = null,
    [property: JsonPropertyName("micAgc")] float? MicAgc = null,
    [property: JsonPropertyName("drive")] float? Drive = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter;
