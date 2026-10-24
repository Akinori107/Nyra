using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>One synced lyrics line as delivered by the lyrics subscription.</summary>
public sealed class SyncedLyricsLine
{
    /// <summary>Line start, in milliseconds.</summary>
    /// <remarks>
    /// Null only from a hostile or future wire that sent none, both live servers always send a number.
    /// Callers that need a number read <c>?? 0</c>. Fractional values are rounded like every other integral
    /// member, a value that is not a number at all fails the read.
    /// </remarks>
    [JsonPropertyName("timestamp"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? Time { get; internal set; }

    /// <summary>
    /// Line duration, in milliseconds, LavaLyrics sends null when unknown, NodeLink always sends a number.
    /// How it is derived depends on the provider, the gap to the next line, the provider's own duration or 0.
    /// </summary>
    /// <remarks>
    /// Fractional values are rounded like every other integral member, a value that is not a number at all
    /// fails the read.
    /// </remarks>
    [JsonPropertyName("duration"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? Duration { get; internal set; }

    /// <summary>The text of the line.</summary>
    /// <remarks>
    /// Absorbs a null the server sends, the getter never hands out null, a missing or nulled text reads as the
    /// empty string.
    /// </remarks>
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
/// The subscription shape carries no synced signal, the REST answer reports <see
/// cref="Nyra.Entities.LyricsData.Synced"/>, the subscription does not, and NodeLink maps text-only lyrics to
/// lines with zeroed timestamps. Lyrics without timing and timing that genuinely starts at zero are
/// indistinguishable here.
/// </remarks>
public sealed class SubscribedLyrics
{
    /// <summary>The name of the source the lyrics came from.</summary>
    /// <remarks>
    /// LavaLyrics reports the lyrics source name, NodeLink reports the track's source name, the actual lyrics
    /// provider sits in <see cref="Provider"/>. Absorbs a null the server sends, the getter never hands out
    /// null, a missing or nulled name reads as the empty string.
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
    /// Lavalink answers LyricsNotFound instead when the provider returned text only, no lyrics, or none with
    /// timing, NodeLink maps text-only lyrics to lines with zeroed timestamps. Null entries the wire carries
    /// are dropped and a null list reads as empty, handlers never see null.
    /// <para>
    /// The drop shifts every line after it, <see cref="LyricsLineEventArgs.LineIndex"/> counts the server's
    /// own array, not this filtered one, so the pair only lines up when the wire carried no nulls.
    /// </para>
    /// </remarks>
    [JsonPropertyName("lines"), JsonInclude]
    public IReadOnlyList<SyncedLyricsLine>? Lines
    {
        get => _lines;
        internal set => _lines = value is null ? [] : [.. value.Where(v => v is not null)];
    }

    private IReadOnlyList<SyncedLyricsLine> _lines = [];
}

/// <summary>The lyrics of the current track were loaded.</summary>
public sealed class LyricsFoundEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The lyrics that were loaded.</summary>
    /// <remarks>
    /// Absorbs a null the server sends, the setter substitutes an empty object, the event reaches user
    /// handlers even when the frame carries none.
    /// </remarks>
    [JsonPropertyName("lyrics"), JsonInclude]
    public SubscribedLyrics Lyrics
    {
        get;
        internal set => field = value ?? new();
    } = new();
}
