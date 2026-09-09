using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>A live stream announced new station metadata while playing.</summary>
public sealed class StreamMetadataUpdatedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    /// <summary>The stream track the metadata belongs to.</summary>
    [JsonPropertyName("track")]
    public Track? Track { get; init; }

    [JsonPropertyName("stream")]
    public StreamMetadata? Stream { get; init; }
}
