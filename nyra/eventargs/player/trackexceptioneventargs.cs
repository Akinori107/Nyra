using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

public sealed class TrackExceptionEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    /// <summary>The track the exception occurred on; null when NodeLink reports the exception after the track already ended.</summary>
    [JsonPropertyName("track")]
    public Track? Track { get; init; }

    [JsonPropertyName("exception")]
    public ErrorInfo Exception { get; init; } = new();
}
