using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The node loaded the SponsorBlock segments of the current track.</summary>
/// <remarks>
/// NodeLink only. The server only fetches segments for YouTube and YouTube Music tracks, and only raises
/// this when it found at least one, overriding segments through the REST API never raises it.
/// </remarks>
public sealed class SponsorBlockSegmentsLoadedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The segments that were loaded, a null list or a null element reads as empty/absent.</summary>
    [JsonPropertyName("segments"), JsonInclude]
    public IReadOnlyList<SponsorBlockSegment> Segments
    {
        get;
        internal set => field = value is null ? [] : [.. value.Where(v => v is not null)];
    } = [];
}
