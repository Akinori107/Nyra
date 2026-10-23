using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The node skipped a SponsorBlock segment of the current track.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed class SponsorBlockSegmentSkippedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>
    /// The track playing when the skip seek completed, null only when it was stopped or reset while the seek
    /// was in flight.
    /// </summary>
    /// <remarks>
    /// A replacement mid-seek is reported as the replacement, a naturally-ended track can still appear, so
    /// attribute the skip by the segment, not by this track.
    /// </remarks>
    [JsonPropertyName("track"), JsonInclude]
    public Track? Track { get; internal set; }

    /// <summary>The segment that was skipped.</summary>
    /// <remarks>
    /// Absorbs a null segment the server sends, the setter substitutes a fresh instance, so handlers never
    /// read null even when the frame carries one.
    /// </remarks>
    [JsonPropertyName("segment"), JsonInclude]
    public SponsorBlockSegment Segment
    {
        get;
        internal set => field = value ?? new();
    } = new();
}
