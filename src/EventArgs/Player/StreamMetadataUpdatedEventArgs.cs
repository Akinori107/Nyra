using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>A live stream announced new station metadata while playing.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed class StreamMetadataUpdatedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The track the metadata belongs to (a live stream), null when stopped.</summary>
    [JsonPropertyName("track"), JsonInclude]
    public Track? Track { get; internal set; }

    /// <summary>The metadata the stream reported, null when the server sent none.</summary>
    [JsonPropertyName("stream"), JsonInclude]
    public StreamMetadata? Stream { get; internal set; }
}
