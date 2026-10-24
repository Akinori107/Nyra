using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Outcome of a lyrics load.</summary>
public enum LyricsLoadType
{
    /// <summary>An outcome the library does not know, reported by a newer server.</summary>
    /// <remarks>
    /// It is the default, a response without a load type reads as Unknown instead of fabricating a known one.
    /// </remarks>
    Unknown,

    /// <summary>A provider had lyrics for the track.</summary>
    Lyrics,

    /// <summary>No provider had lyrics for the track.</summary>
    Empty,

    /// <summary>The lyrics load failed, other providers may not have been tried.</summary>
    Error,
}

/// <summary>One timed line of a lyrics result.</summary>
/// <remarks>
/// Plain lyrics providers report theirs with a zero time and duration, this shape comes from NodeLink's load
/// response.
/// </remarks>
public sealed record LyricsLine
{
    /// <summary>The text of the line.</summary>
    [JsonPropertyName("text"), JsonInclude]
    public string Text
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Line start, in milliseconds.</summary>
    [JsonPropertyName("time"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long Time { get; internal set; }

    /// <summary>Line duration, in milliseconds, 0 when the server does not know it.</summary>
    [JsonPropertyName("duration"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long Duration { get; internal set; }

    /// <summary>Per-word timings, when the provider supplies them.</summary>
    [JsonPropertyName("words"), JsonInclude]
    public JsonElement? Words { get; internal set; }

    /// <summary>Indicates whether the lines carry the same text, timing and raw words.</summary>
    /// <param name="other">The line to compare with.</param>
    /// <returns>Whether the members match, the raw words compared by content.</returns>
    public bool Equals(LyricsLine? other) =>
        other is not null
        && Text == other.Text
        && Time == other.Time
        && Duration == other.Duration
        && ContentEquality.JsonElementEquals(Words, other.Words);

    /// <summary>Returns a hash of the line's comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Text, Time, Duration);
}

/// <summary>Lyrics of a track, as resolved by the node's providers.</summary>
public sealed record LyricsData
{
    /// <summary>Display label.</summary>
    /// <remarks>
    /// NodeLink sends the track title or a provider variant label, LavaLyrics the lyrics source name.
    /// </remarks>
    [JsonPropertyName("name"), JsonInclude]
    public string? Name { get; internal set; }

    /// <summary>Whether the lines carry timing information.</summary>
    [JsonPropertyName("synced"), JsonInclude]
    public bool? Synced { get; internal set; }

    /// <summary>
    /// The timed lines of the lyrics, null when there are none, plain providers can also report an empty list.
    /// </summary>
    [JsonPropertyName("lines"), JsonInclude]
    public IReadOnlyList<LyricsLine>? Lines
    {
        get;
        internal set => field = value is null ? null : [.. value.Where(v => v is not null)];
    }

    /// <summary>The provider the lyrics came from.</summary>
    [JsonPropertyName("provider"), JsonInclude]
    public string? Provider { get; internal set; }

    /// <summary>Plain lyrics text, when there are no timed lines, filled by LavaLyrics.</summary>
    [JsonPropertyName("text"), JsonInclude]
    public string? Text { get; internal set; }

    /// <summary>Present only when the load failed with <see cref="LyricsLoadType.Error"/>.</summary>
    [JsonPropertyName("message"), JsonInclude]
    public string? Message { get; internal set; }

    /// <summary>Present only when the load failed with <see cref="LyricsLoadType.Error"/>.</summary>
    [JsonPropertyName("severity"), JsonInclude]
    public ErrorSeverity? Severity { get; internal set; }

    /// <summary>Indicates whether the lyrics carry the same members.</summary>
    /// <param name="other">The lyrics to compare with.</param>
    /// <returns>Whether the members match, the timed lines compared by content.</returns>
    public bool Equals(LyricsData? other) =>
        other is not null
        && Name == other.Name
        && Synced == other.Synced
        && Provider == other.Provider
        && Text == other.Text
        && Message == other.Message
        && Severity == other.Severity
        && ContentEquality.ListEquals(Lines, other.Lines);

    /// <summary>Returns the lyrics data with the normalized line count.</summary>
    /// <returns>The data's public members, the raw backing field stays out of it.</returns>
    public override string ToString() =>
        $"LyricsData {{ Name = {Name}, Synced = {Synced}, Provider = {Provider}, Text = {Text ?? "null"}, Message = {Message ?? "null"}, Severity = {Severity?.ToString() ?? "null"}, Lines = {Lines?.Count ?? 0} lines }}";

    /// <summary>Returns a hash of the lyrics' comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Name, Synced, Provider, Text, Message, Severity);
}

/// <summary>Result of a lyrics load, the data shape depends on <see cref="LoadType"/>.</summary>
public sealed record LyricsResult
{
    /// <summary>The outcome of the lyrics load.</summary>
    [JsonPropertyName("loadType"), JsonInclude]
    public LyricsLoadType LoadType { get; internal set; }

    /// <summary>
    /// The lyrics on <see cref="LyricsLoadType.Lyrics"/>, the error details on Error, an empty object on Empty
    /// (NodeLink).
    /// </summary>
    /// <remarks>
    /// On <see cref="LyricsLoadType.Unknown"/> it holds whatever the server put in the envelope's data slot,
    /// if anything. Null on Lavalink, whose 204 carries no body.
    /// </remarks>
    [JsonPropertyName("data"), JsonInclude]
    public LyricsData? Data { get; internal set; }

    /// <summary>Indicates whether the results carry the same load type and data.</summary>
    /// <param name="other">The result to compare with.</param>
    /// <returns>Whether the load type matches and the data compares equal.</returns>
    public bool Equals(LyricsResult? other) =>
        other is not null
        && LoadType == other.LoadType
        && EqualityComparer<LyricsData?>.Default.Equals(Data, other.Data);

    /// <summary>Returns a hash of the result's comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(LoadType, Data);
}

internal sealed record LavaLyricsLyrics(
    [property: JsonPropertyName("sourceName")] string SourceName,
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("lines")] IReadOnlyList<LavaLyricsLine>? Lines
)
{
    public bool Equals(LavaLyricsLyrics? other) =>
        other is not null
        && SourceName == other.SourceName
        && Provider == other.Provider
        && Text == other.Text
        && ContentEquality.ListEquals(Lines, other.Lines);

    public override int GetHashCode() => HashCode.Combine(SourceName, Provider, Text);

    public LyricsResult ToResult()
    {
        var lines = Lines?.Where(line => line is not null).ToList();

        return new LyricsResult
        {
            LoadType = LyricsLoadType.Lyrics,
            Data = new LyricsData
            {
                Name = SourceName,
                Synced = lines is { Count: > 0 },
                Lines = lines
                    ?.Select(line => new LyricsLine
                    {
                        Text = line.Line,
                        Time = line.Timestamp,
                        Duration = line.Duration ?? 0,
                    })
                    .ToList(),
                Provider = Provider,
                Text = Text,
            },
        };
    }
}

internal sealed record LavaLyricsLine(
    [property: JsonPropertyName("timestamp")]
    [property: JsonConverter(typeof(RoundedInt64Converter))]
        long Timestamp,
    [property: JsonPropertyName("duration")]
    [property: JsonConverter(typeof(RoundedInt64Converter))]
        long? Duration,
    [property: JsonPropertyName("line")] string Line
);
