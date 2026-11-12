using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>Raised when a mix started playing on top of the main track.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed class MixStartedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The id of the mix layer that started.</summary>
    [JsonPropertyName("mixId"), JsonInclude]
    public string MixId
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>The track the layer plays.</summary>
    /// <remarks>Never null, a missing value reads as an empty instance.</remarks>
    [JsonPropertyName("track"), JsonInclude]
    public Track Track
    {
        get;
        internal set => field = value ?? new();
    } = new();

    /// <summary>Layer loudness, 0.0-1.0, as the server reported it.</summary>
    [JsonPropertyName("volume"), JsonInclude]
    [JsonConverter(typeof(FiniteFloatConverter))]
    public float Volume { get; internal set; }
}
