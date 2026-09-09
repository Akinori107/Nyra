using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

public sealed class TrackStuckEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    /// <summary>The stuck track, null when the server did not include it.</summary>
    [JsonPropertyName("track")]
    public Track? Track { get; init; }

    /// <summary>The stuck threshold, in milliseconds, that was exceeded.</summary>
    [JsonPropertyName("thresholdMs")]
    public long ThresholdMilliseconds { get; init; }

    /// <summary>What the node reported as the cause, NodeLink only, null on Lavalink.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
