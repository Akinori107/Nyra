using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>One synced lyrics line as delivered by the lyrics subscription.</summary>
public sealed class SyncedLyricsLine
{
    /// <summary>Line start, in milliseconds.</summary>
    [JsonPropertyName("timestamp")]
    public long Time { get; init; }

    /// <summary>Line duration, in milliseconds, null when the server does not know it.</summary>
    [JsonPropertyName("duration")]
    public long? Duration { get; init; }

    [JsonPropertyName("line")]
    public string Text { get; init; } = string.Empty;

    /// <summary>Per-word timings, when the provider supplies them.</summary>
    [JsonPropertyName("words")]
    public JsonElement? Words { get; init; }
}

/// <summary>The full lyrics of the current track, as loaded by a lyrics subscription.</summary>
public sealed class SubscribedLyrics
{
    [JsonPropertyName("sourceName")]
    public string SourceName { get; init; } = string.Empty;

    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    /// <summary>The lyrics as plain text, NodeLink joins the lines, LavaLyrics sends it directly.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    /// <summary>Timed lines, null when the provider returned text only.</summary>
    [JsonPropertyName("lines")]
    public IReadOnlyList<SyncedLyricsLine>? Lines { get; init; }
}

public sealed class LyricsFoundEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    [JsonPropertyName("lyrics")]
    public SubscribedLyrics Lyrics { get; init; } = new();
}
