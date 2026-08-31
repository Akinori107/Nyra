using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

public sealed class TrackFinishedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    /// <summary>The track that ended; null when NodeLink reports the end after the track was already reset.</summary>
    [JsonPropertyName("track")]
    public Track? Track { get; init; }

    [JsonPropertyName("reason")]
    public TrackEndReason EndReason { get; init; }
}
