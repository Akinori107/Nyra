using System.Text.Json;
using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>One synced lyrics line as delivered by the lyrics subscription.</summary>
public sealed class SyncedLyricsLine
{
    /// <summary>Line start, in milliseconds.</summary>
    /// <remarks>
    /// Both servers always send it.
    /// </remarks>
    [JsonPropertyName("timestamp"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? Time { get; internal set; }

    /// <summary>
    /// Line duration, in milliseconds, LavaLyrics sends null when unknown.
    /// </summary>
    [JsonPropertyName("duration"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? Duration { get; internal set; }

    /// <summary>The text of the line.</summary>
    /// <remarks>Never null, a missing value reads as an empty string.</remarks>
    [JsonPropertyName("line"), JsonInclude]
    public string Text
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Per-word timings, when the provider supplies them.</summary>
    [JsonPropertyName("words"), JsonInclude]
    public JsonElement? Words { get; internal set; }
}

/// <summary>The full lyrics of the current track, as loaded by a lyrics subscription.</summary>
/// <remarks>
/// Plain lyrics arrive as lines starting at zero, so they look the same as lyrics that start at zero.
/// </remarks>
public sealed class SubscribedLyrics
{
    /// <summary>The name of the source the lyrics came from.</summary>
    /// <remarks>
    /// The actual provider is <see cref="Provider"/>. Never null, a missing value reads as an empty string.
    /// </remarks>
    [JsonPropertyName("sourceName"), JsonInclude]
    public string SourceName
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>The provider the lyrics came from.</summary>
    [JsonPropertyName("provider"), JsonInclude]
    public string? Provider { get; internal set; }

    /// <summary>The lyrics as plain text, NodeLink joins the lines, LavaLyrics sends it directly.</summary>
    [JsonPropertyName("text"), JsonInclude]
    public string? Text { get; internal set; }

    /// <summary>Timed lines, both servers deliver them on this event.</summary>
    /// <remarks>
    /// Lavalink sends LyricsNotFound for plain lyrics instead, NodeLink sends them as lines starting at zero.
    /// Never null.
    /// </remarks>
    [JsonPropertyName("lines"), JsonInclude]
    public IReadOnlyList<SyncedLyricsLine>? Lines
    {
        get;
        internal set => field = ContentEquality.WithoutNulls(value);
    } = [];
}

/// <summary>The lyrics of the current track were loaded.</summary>
public sealed class LyricsFoundEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The lyrics that were loaded.</summary>
    /// <remarks>Never null, a missing value reads as an empty instance.</remarks>
    [JsonPropertyName("lyrics"), JsonInclude]
    public SubscribedLyrics Lyrics
    {
        get;
        internal set => field = value ?? new();
    } = new();
}
