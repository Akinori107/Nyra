using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>EternalBox context of the track that just started playing.</summary>
public sealed class EternalBoxInfoUpdatedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    /// <summary>The EternalBox track the info belongs to.</summary>
    [JsonPropertyName("track")]
    public Track? Track { get; init; }

    [JsonPropertyName("eternalbox")]
    public EternalBoxInfo? Info { get; init; }
}
