using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Statistics of an audio node, from the <c>stats</c> websocket operation or <c>GET /v4/stats</c>.</summary>
public sealed record Stats
{
    /// <summary>The players the server hosts.</summary>
    [JsonPropertyName("players"), JsonInclude]
    public int Players { get; internal set; }

    /// <summary>The players with a track playing.</summary>
    [JsonPropertyName("playingPlayers"), JsonInclude]
    public int PlayingPlayers { get; internal set; }

    /// <summary>Uptime in milliseconds.</summary>
    [JsonPropertyName("uptime"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long Uptime { get; internal set; }

    /// <summary>How long the server has been running.</summary>
    /// <remarks>
    /// <see cref="TimeSpan.Zero"/> for a value outside the range a <see cref="TimeSpan"/> can represent, the raw
    /// <see cref="Uptime"/> still carries it. The zero is indistinguishable from a node that just started.
    /// </remarks>
    [JsonIgnore]
    public TimeSpan UptimeDuration => TrackInfo.ToTimeSpan(Uptime);

    /// <summary>Memory statistics of the server.</summary>
    /// <remarks>Absorbs a null the server sends, the setter substitutes an empty instance.</remarks>
    [JsonPropertyName("memory"), JsonInclude]
    public MemoryStats Memory
    {
        get;
        internal set => field = value ?? new();
    } = new();

    /// <summary>CPU statistics of the server.</summary>
    /// <remarks>Absorbs a null the server sends, the setter substitutes an empty instance.</remarks>
    [JsonPropertyName("cpu"), JsonInclude]
    public CpuStats Cpu
    {
        get;
        internal set => field = value ?? new();
    } = new();

    /// <summary>Null when the server reports no frame statistics.</summary>
    /// <remarks>
    /// NodeLink sends the member as null when no player exists at all, it does not omit the field.
    /// <para>
    /// Lavalink sends it as null over REST, where it has no session to read frame counters from, and on the
    /// websocket until the session has playing players with a full minute of continuous playback. A non-zero
    /// playing count can still arrive with null frames.
    /// </para>
    /// </remarks>
    [JsonPropertyName("frameStats"), JsonInclude]
    public FrameStats? FrameStats { get; internal set; }

    /// <summary>
    /// In-memory counter snapshot NodeLink appends to <c>GET /v4/stats</c>, request and error totals per API
    /// endpoint, success and failure totals per source, and playback event totals.
    /// </summary>
    /// <remarks>
    /// Null on Lavalink and on the websocket path, the counters are lifetime totals reset only by a server
    /// restart.
    /// </remarks>
    [JsonPropertyName("detailedStats"), JsonInclude]
    public DetailedStats? DetailedStats { get; internal set; }
}

/// <summary>Per-domain counter snapshot of NodeLink's stats manager, REST only.</summary>
public sealed record DetailedStats
{
    /// <summary>API request and error counters.</summary>
    [JsonPropertyName("api"), JsonInclude]
    public ApiCounters? Api { get; internal set; }

    /// <summary>Success and failure counters per source name.</summary>
    /// <remarks>Null values the wire carries are dropped.</remarks>
    [JsonPropertyName("sources"), JsonInclude]
    public IReadOnlyDictionary<string, SourceCounters>? Sources
    {
        get;
        internal set => field = value?.Where(pair => pair.Value is not null).ToDictionary();
    }

    /// <summary>Playback event counters by event name.</summary>
    [JsonPropertyName("playback"), JsonInclude]
    public PlaybackCounters? Playback { get; internal set; }

    /// <summary>Indicates whether both snapshots carry the same counters.</summary>
    /// <param name="other">The snapshot to compare with.</param>
    /// <returns>Whether the members match, the counters compared by content.</returns>
    public bool Equals(DetailedStats? other) =>
        other is not null
        && Api == other.Api
        && Playback == other.Playback
        && ContentEquality.DictionaryEquals(Sources, other.Sources);

    /// <summary>Returns a hash of the nested counters, the source map stays out of it.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Api, Playback);
}

/// <summary>API counters of the detailed stats snapshot.</summary>
public sealed record ApiCounters
{
    /// <summary>
    /// Request totals per endpoint, an <c>others</c> bucket collects the overflow past the endpoint cap.
    /// </summary>
    /// <remarks>
    /// A null value for an endpoint throws on parse, unlike the source counters, which drop their null values.
    /// </remarks>
    [JsonPropertyName("requests"), JsonInclude]
    public IReadOnlyDictionary<string, long>? Requests { get; internal set; }

    /// <summary>
    /// Error totals per endpoint, an <c>others</c> bucket collects the overflow past the endpoint cap.
    /// </summary>
    /// <remarks>
    /// A null value for an endpoint throws on parse, unlike the source counters, which drop their null values.
    /// </remarks>
    [JsonPropertyName("errors"), JsonInclude]
    public IReadOnlyDictionary<string, long>? Errors { get; internal set; }

    /// <summary>Indicates whether both counters carry the same totals.</summary>
    /// <param name="other">The counters to compare with.</param>
    /// <returns>Whether the maps match, compared by content.</returns>
    public bool Equals(ApiCounters? other) =>
        other is not null
        && ContentEquality.DictionaryEquals(Requests, other.Requests)
        && ContentEquality.DictionaryEquals(Errors, other.Errors);

    /// <summary>Returns a hash of the counters, the maps stay out of it.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => 0;
}

/// <summary>Per-source counters of the detailed stats snapshot.</summary>
public sealed record SourceCounters
{
    /// <summary>Successful requests for the source.</summary>
    [JsonPropertyName("success"), JsonInclude]
    public long Success { get; internal set; }

    /// <summary>Failed requests for the source.</summary>
    [JsonPropertyName("failure"), JsonInclude]
    public long Failure { get; internal set; }
}

/// <summary>Playback counters of the detailed stats snapshot.</summary>
public sealed record PlaybackCounters
{
    /// <summary>Playback event totals by event name.</summary>
    /// <remarks>
    /// A null value for an event name throws on parse, unlike the source counters, which drop their null
    /// values.
    /// </remarks>
    [JsonPropertyName("events"), JsonInclude]
    public IReadOnlyDictionary<string, long>? Events { get; internal set; }

    /// <summary>Indicates whether both counters carry the same event totals.</summary>
    /// <param name="other">The counters to compare with.</param>
    /// <returns>Whether the maps match, compared by content.</returns>
    public bool Equals(PlaybackCounters? other) =>
        other is not null && ContentEquality.DictionaryEquals(Events, other.Events);

    /// <summary>Returns a hash of the counters, the map stays out of it.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => 0;
}

/// <summary>Memory statistics of an audio node, in bytes.</summary>
/// <remarks>
/// Lavalink reports the JVM process (runtime free, total, max), NodeLink reports host-level free and total
/// for <see cref="Free"/> and <see cref="Reservable"/> and the process heap for <see cref="Used"/> and <see
/// cref="Allocated"/>, the members are not comparable across the two servers.
/// </remarks>
public sealed record MemoryStats
{
    /// <summary>The free memory of the server.</summary>
    [JsonPropertyName("free"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long Free { get; internal set; }

    /// <summary>The used memory of the server.</summary>
    [JsonPropertyName("used"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long Used { get; internal set; }

    /// <summary>The memory allocated by the server.</summary>
    [JsonPropertyName("allocated"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long Allocated { get; internal set; }

    /// <summary>The memory ceiling the server process may still grow to.</summary>
    [JsonPropertyName("reservable"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long Reservable { get; internal set; }
}

/// <summary>CPU load statistics of the server.</summary>
public sealed record CpuStats
{
    /// <summary>The cores the server runs on.</summary>
    [JsonPropertyName("cores"), JsonInclude]
    public int Cores { get; internal set; }

    /// <summary>System load.</summary>
    /// <remarks>
    /// Lavalink normalizes to 0.0-1.0, NodeLink reports the raw OS load average, which can exceed 1.0.
    /// </remarks>
    [JsonPropertyName("systemLoad"), JsonInclude]
    public double SystemLoad { get; internal set; }

    /// <summary>Load caused by the audio node itself, 0.0-1.0.</summary>
    /// <remarks>
    /// NodeLink omits this member entirely (the 0.0 is the client default for a missing field), the value is
    /// indistinguishable from an idle node there, watch <see cref="SystemLoad"/> or <see cref="NodelinkLoad"/>
    /// instead.
    /// </remarks>
    [JsonPropertyName("lavalinkLoad"), JsonInclude]
    public double LavalinkLoad { get; internal set; }

    /// <summary>The node load as NodeLink reports it, under this member instead of the Lavalink one.</summary>
    /// <remarks>
    /// A single-process NodeLink can leave it at 0, watch <see cref="SystemLoad"/> there, cluster or worker
    /// mode aggregates the worker loads. <see cref="NodeLoad"/> reads whichever field the server reports.
    /// </remarks>
    [JsonPropertyName("nodelinkLoad"), JsonInclude]
    public double? NodelinkLoad { get; internal set; }

    /// <summary>The load of the node process, from whatever field the server reports.</summary>
    /// <remarks>
    /// A server that reports both members (a proxy or a fork filling the Lavalink shape too) is read from the
    /// NodeLink field first, <see cref="LavalinkLoad"/> is the fallback for servers that only fill it.
    /// </remarks>
    [JsonIgnore]
    public double NodeLoad => NodelinkLoad ?? LavalinkLoad;
}

/// <summary>
/// Audio frame statistics of the server. The window is the last minute on Lavalink, lifetime totals per voice
/// connection on NodeLink, the members are not comparable across the two servers.
/// </summary>
public sealed record FrameStats
{
    /// <summary>The frames sent to Discord, in the last minute on Lavalink, lifetime on NodeLink.</summary>
    [JsonPropertyName("sent"), JsonInclude]
    public long Sent { get; internal set; }

    /// <summary>
    /// The frames that went out as silence in the last minute, on NodeLink the packets that never went out at
    /// all.
    /// </summary>
    [JsonPropertyName("nulled"), JsonInclude]
    public long Nulled { get; internal set; }

    /// <summary>The frames that were due but not sent in the last minute.</summary>
    [JsonPropertyName("deficit"), JsonInclude]
    public long Deficit { get; internal set; }

    /// <summary>Frames the node expected to send, always null on Lavalink.</summary>
    /// <remarks>
    /// NodeLink's frame counts are lifetime totals accumulated since each voice connection started, Lavalink
    /// reports last-minute averages per player.
    /// </remarks>
    [JsonPropertyName("expected"), JsonInclude]
    public long? Expected { get; internal set; }
}
