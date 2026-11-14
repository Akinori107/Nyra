using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The current track ended.</summary>
public sealed class TrackFinishedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The track that ended, null when NodeLink reports the end after the track was already reset.</summary>
    /// <remarks>
    /// A null does not tell which track ended.
    /// <para>
    /// The track keeps the position it ended at, so playing it again may start from there. Play the original
    /// instance, or set <see cref="PlayOptions.StartPosition"/> to <see cref="TimeSpan.Zero"/>.
    /// </para>
    /// </remarks>
    [JsonPropertyName("track"), JsonInclude]
    public Track? Track { get; internal set; }

    /// <summary>
    /// Why the track ended, see <see cref="TrackEndReason"/> for which ends may start the next track.
    /// </summary>
    /// <remarks>
    /// Unknown when the server sent none.
    /// </remarks>
    [JsonPropertyName("reason"), JsonInclude]
    public TrackEndReason EndReason { get; internal set; } = TrackEndReason.Unknown;
}
