using System.Text.Json.Serialization;

namespace Nyra.Entities.Filters;

/// <summary>Simulates a room's reflections. NodeLink only.</summary>
/// <param name="RoomSize">Room size, 0.0-1.0.</param>
/// <param name="Damping">How much the room absorbs, 0.0-1.0.</param>
/// <param name="WetLevel">Reverb loudness, 0.0-1.0.</param>
/// <param name="DryLevel">Original-signal loudness, 0.0-1.0.</param>
/// <param name="Width">Stereo width of the reverb, 0.0-1.0.</param>
/// <param name="Mix">Overall reverb mix, 0.0-1.0.</param>
/// <param name="Transition">Animates the change instead of stepping it.</param>
public sealed record ReverbFilter(
    [property: JsonPropertyName("roomSize")] float? RoomSize = null,
    [property: JsonPropertyName("damping")] float? Damping = null,
    [property: JsonPropertyName("wetLevel")] float? WetLevel = null,
    [property: JsonPropertyName("dryLevel")] float? DryLevel = null,
    [property: JsonPropertyName("width")] float? Width = null,
    [property: JsonPropertyName("mix")] float? Mix = null,
    [property: JsonPropertyName("transition")] FilterTransition? Transition = null
) : IFilter;
