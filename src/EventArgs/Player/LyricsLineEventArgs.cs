using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>A lyrics line became current during playback.</summary>
public sealed class LyricsLineEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>
    /// The index of the line in the lyrics.
    /// </summary>
    [JsonPropertyName("lineIndex"), JsonInclude]
    public int LineIndex { get; internal set; }

    /// <summary>The line that became current.</summary>
    /// <remarks>Never null, a missing value reads as an empty instance.</remarks>
    [JsonPropertyName("line"), JsonInclude]
    public SyncedLyricsLine Line
    {
        get => field ??= new();
        internal set;
    }

    /// <summary>
    /// True when the line came late, like after a seek or a subscription that started mid-track.
    /// </summary>
    [JsonPropertyName("skipped"), JsonInclude]
    public bool Skipped { get; internal set; }
}
