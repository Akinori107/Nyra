using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The current track ended.</summary>
public sealed class TrackFinishedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The track that ended, null when NodeLink reports the end after the track was already reset.</summary>
    /// <remarks>
    /// A null carries no identity, it does not name the track that died and must not be read as certainly the
    /// current one, the reset the server reports may belong to an earlier playthrough.
    /// </remarks>
    [JsonPropertyName("track"), JsonInclude]
    public Track? Track { get; internal set; }

    /// <summary>
    /// Why the track ended, see <see cref="TrackEndReason"/> for which ends may start the next track.
    /// </summary>
    /// <remarks>
    /// Defaults to the unknown sentinel, a frame without the member never claims the track finished cleanly.
    /// </remarks>
    [JsonPropertyName("reason"), JsonInclude]
    public TrackEndReason EndReason { get; internal set; } = TrackEndReason.Unknown;
}
