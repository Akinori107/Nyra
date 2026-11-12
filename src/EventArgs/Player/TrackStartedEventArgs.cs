using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The server started playing the current track.</summary>
public sealed class TrackStartedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The track that started.</summary>
    /// <remarks>Never null, a missing value reads as an empty instance.</remarks>
    [JsonPropertyName("track"), JsonInclude]
    public Track Track
    {
        get => field ??= new();
        internal set;
    }

    /// <summary>The itag of the audio format the track is playing with, NodeLink only.</summary>
    /// <remarks>
    /// Usually null, only some plugin sources set it.
    /// </remarks>
    [JsonPropertyName("playingQuality"), JsonInclude]
    [JsonConverter(typeof(RoundedInt32Converter))]
    public int? PlayingQuality { get; internal set; }
}
