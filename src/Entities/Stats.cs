using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Statistics of an audio node, delivered through <c>stats</c> websocket operations.</summary>
public sealed record Stats
{
    [JsonPropertyName("players")]
    public int Players { get; init; }

    [JsonPropertyName("playingPlayers")]
    public int PlayingPlayers { get; init; }

    /// <summary>Uptime in milliseconds.</summary>
    [JsonPropertyName("uptime")]
    public long Uptime { get; init; }

    [JsonIgnore]
    public TimeSpan UptimeDuration =>
        Uptime is >= 0 and <= TrackInfo.MaxTimeSpanMilliseconds
            ? TimeSpan.FromMilliseconds(Uptime)
            : TimeSpan.Zero;

    [JsonPropertyName("memory")]
    public Memory Memory { get; init; } = new();

    [JsonPropertyName("cpu")]
    public Cpu Cpu { get; init; } = new();

    /// <summary>Null when there are no playing players on the session.</summary>
    [JsonPropertyName("frameStats")]
    public FrameStats? FrameStats { get; init; }
}

/// <summary>Memory statistics of an audio node, in bytes.</summary>
public sealed record Memory
{
    [JsonPropertyName("free")]
    public long Free { get; init; }

    [JsonPropertyName("used")]
    public long Used { get; init; }

    [JsonPropertyName("allocated")]
    public long Allocated { get; init; }

    [JsonPropertyName("reservable")]
    public long Reservable { get; init; }
}

public sealed record Cpu
{
    [JsonPropertyName("cores")]
    public int Cores { get; init; }

    /// <summary>System load. Lavalink normalizes to 0.0-1.0, NodeLink reports the raw OS load average, which can exceed 1.0.</summary>
    [JsonPropertyName("systemLoad")]
    public double SystemLoad { get; init; }

    /// <summary>Load caused by the audio node itself, 0.0-1.0.</summary>
    [JsonPropertyName("lavalinkLoad")]
    public double LavalinkLoad { get; init; }

    /// <summary>NodeLink reports the node load under this member instead.</summary>
    [JsonPropertyName("nodelinkLoad")]
    public double? NodelinkLoad { get; init; }

    [JsonIgnore]
    public double NodeLoad => NodelinkLoad ?? LavalinkLoad;
}

public sealed record FrameStats
{
    [JsonPropertyName("sent")]
    public long Sent { get; init; }

    [JsonPropertyName("nulled")]
    public long Nulled { get; init; }

    [JsonPropertyName("deficit")]
    public long Deficit { get; init; }

    /// <summary>Frames the node expected to send in the interval, null when the server does not report it.</summary>
    [JsonPropertyName("expected")]
    public long? Expected { get; init; }
}
