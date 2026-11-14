using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The node loaded the SponsorBlock segments of the current track.</summary>
/// <remarks>
/// NodeLink only. Raised only for YouTube tracks with at least one segment.
/// </remarks>
public sealed class SponsorBlockSegmentsLoadedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The segments that were loaded, never null.</summary>
    [JsonPropertyName("segments"), JsonInclude]
    public IReadOnlyList<SponsorBlockSegment> Segments
    {
        get;
        internal set => field = ContentEquality.WithoutNulls(value);
    } = [];
}
