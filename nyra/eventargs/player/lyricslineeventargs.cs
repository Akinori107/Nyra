using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

public sealed class LyricsLineEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    [JsonPropertyName("lineIndex")]
    public int LineIndex { get; init; }

    [JsonPropertyName("line")]
    public SyncedLyricsLine Line { get; init; } = new();

    /// <summary>Whether the line arrived late and playback had already moved past it.</summary>
    [JsonPropertyName("skipped")]
    public bool Skipped { get; init; }
}
