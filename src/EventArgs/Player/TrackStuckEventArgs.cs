using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The current track got stuck.</summary>
public sealed class TrackStuckEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    /// <remarks>
    /// A frame without the member reads as 0, which matches no connection, the event is dropped. A JSON null
    /// reads as 0 the same way, one hostile frame must not kill the event or the frame.
    /// </remarks>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>
    /// The stuck track, null when the server did not include it, e.g. NodeLink reporting the stall after the
    /// track was already reset.
    /// </summary>
    [JsonPropertyName("track"), JsonInclude]
    public Track? Track { get; internal set; }

    /// <summary>The stuck threshold, in milliseconds, that was exceeded.</summary>
    /// <remarks>
    /// Null when the frame carried none or sent an explicit null. Fractional values are rounded, a value that
    /// is not a number at all drops the event whole, and so does a number outside the int64 range, except the
    /// stream marker, which clamps to the maximum.
    /// </remarks>
    [JsonPropertyName("thresholdMs"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? ThresholdMilliseconds { get; internal set; }

    /// <summary>What the node reported as the cause, NodeLink only, null on Lavalink.</summary>
    [JsonPropertyName("reason"), JsonInclude]
    public string? Reason { get; internal set; }
}
