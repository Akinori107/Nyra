using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The node skipped a SponsorBlock segment of the current track.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed class SponsorBlockSegmentSkippedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>
    /// The track playing when the skip finished, null when it was stopped meanwhile.
    /// </summary>
    /// <remarks>
    /// It can be a different track than the one skipped, so go by <see cref="Segment"/>.
    /// </remarks>
    [JsonPropertyName("track"), JsonInclude]
    public Track? Track { get; internal set; }

    /// <summary>The segment that was skipped.</summary>
    /// <remarks>Never null, a missing value reads as an empty instance.</remarks>
    [JsonPropertyName("segment"), JsonInclude]
    public SponsorBlockSegment Segment
    {
        get;
        internal set => field = value ?? new();
    } = new();
}
