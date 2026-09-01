using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

public sealed class SponsorBlockSegmentsLoadedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    [JsonPropertyName("segments")]
    public IReadOnlyList<SponsorBlockSegment> Segments { get; init; } = [];
}
