using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The server started playing the current track.</summary>
public sealed class TrackStartedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    private Track? _track;

    /// <summary>The track that started.</summary>
    /// <remarks>
    /// Absorbs a null track the server sends, the getter materializes a fresh instance, so handlers never read
    /// null even when the frame carries one. The materialized instance is not thread-safe to race on,
    /// capture the value once.
    /// </remarks>
    [JsonPropertyName("track"), JsonInclude]
    public Track Track
    {
        get => _track ??= new();
        internal set => _track = value;
    }

    /// <summary>The itag of the audio format the track is playing with, NodeLink only.</summary>
    /// <remarks>
    /// Non-null only when the source resolved the format to an itag. NodeLink's built-in sources leave it
    /// null, they resolve formats to plain strings, tagless objects (SoundCloud's HLS), or none, only custom
    /// plugin sources resolve to an itag, which travels here as its number.
    /// <para>A fractional echo rounds like every other integral member.</para>
    /// </remarks>
    [JsonPropertyName("playingQuality"), JsonInclude]
    [JsonConverter(typeof(RoundedInt32Converter))]
    public int? PlayingQuality { get; internal set; }
}
