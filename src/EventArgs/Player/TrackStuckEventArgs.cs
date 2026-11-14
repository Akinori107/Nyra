using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The current track got stuck.</summary>
public sealed class TrackStuckEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>
    /// The stuck track, null when the server did not send it.
    /// </summary>
    [JsonPropertyName("track"), JsonInclude]
    public Track? Track { get; internal set; }

    /// <summary>The stuck threshold, in milliseconds, that was exceeded.</summary>
    /// <remarks>
    /// Null when the server sent none.
    /// </remarks>
    [JsonPropertyName("thresholdMs"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? ThresholdMilliseconds { get; internal set; }

    /// <summary>What the node reported as the cause, NodeLink only, null on Lavalink.</summary>
    [JsonPropertyName("reason"), JsonInclude]
    public string? Reason { get; internal set; }
}
