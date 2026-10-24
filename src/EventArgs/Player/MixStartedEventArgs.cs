using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>Raised when a mix started playing on top of the main track.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed class MixStartedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The id of the mix layer that started.</summary>
    [JsonPropertyName("mixId"), JsonInclude]
    public string MixId
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>The track the layer plays.</summary>
    /// <remarks>
    /// Absorbs a null track the server sends, the setter substitutes a fresh instance, so handlers never read
    /// null even when the frame carries one.
    /// </remarks>
    [JsonPropertyName("track"), JsonInclude]
    public Track Track
    {
        get;
        internal set => field = value ?? new();
    } = new();

    /// <summary>Layer loudness, 0.0-1.0, as reported, the server enforces the range, the read does not clamp.</summary>
    /// <remarks>
    /// The converter fails only a non-finite echo inside the read (1e40 reads back non-finite), an in-range
    /// value outside 0.0-1.0 is not rejected here.
    /// </remarks>
    [JsonPropertyName("volume"), JsonInclude]
    [JsonConverter(typeof(FiniteFloatConverter))]
    public float Volume { get; internal set; }
}
