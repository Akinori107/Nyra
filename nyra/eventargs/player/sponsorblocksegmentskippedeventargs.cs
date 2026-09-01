using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

public sealed class SponsorBlockSegmentSkippedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    [JsonPropertyName("track")]
    public Track Track { get; init; } = new();

    [JsonPropertyName("segment")]
    public SponsorBlockSegment Segment { get; init; } = new();
}
