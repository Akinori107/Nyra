using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>A lyrics line became current during playback.</summary>
public sealed class LyricsLineEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>
    /// The index of the line in the lyrics, counting the lines exactly as the server's array carries them, a
    /// null entry the wire carried does not shift it, while <see cref="LyricsFoundEventArgs"/> drops null
    /// entries from the list it exposes, so the index only addresses that filtered list when the wire carried
    /// no nulls.
    /// </summary>
    [JsonPropertyName("lineIndex"), JsonInclude]
    public int LineIndex { get; internal set; }

    /// <summary>The line that became current.</summary>
    /// <remarks>
    /// Absorbs a null the server sends, the getter materializes an empty line, the event reaches user handlers
    /// even when the frame carries none.
    /// </remarks>
    [JsonPropertyName("line"), JsonInclude]
    public SyncedLyricsLine Line
    {
        get => _line ??= new();
        internal set => _line = value;
    }

    private SyncedLyricsLine? _line;

    /// <summary>
    /// True when the line is out of step with playback, the event was emitted after the line's timestamp had
    /// already passed, or the playback position jumped past earlier lines (a seek, or the subscription
    /// starting mid-track).
    /// </summary>
    [JsonPropertyName("skipped"), JsonInclude]
    public bool Skipped { get; internal set; }
}
