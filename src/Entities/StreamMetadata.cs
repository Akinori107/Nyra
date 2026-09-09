using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Shoutcast/Icecast metadata of a live stream, reported while the stream plays. NodeLink only.</summary>
public sealed record IcyMetadata
{
    /// <summary>Raw metadata block, as it arrived on the wire.</summary>
    [JsonPropertyName("raw")]
    public string? Raw { get; init; }

    [JsonPropertyName("streamTitle")]
    public string? StreamTitle { get; init; }

    [JsonPropertyName("streamUrl")]
    public string? StreamUrl { get; init; }

    /// <summary>Every <c>key='value'</c> pair of the metadata block, keys lowercased.</summary>
    [JsonPropertyName("fields")]
    public IReadOnlyDictionary<string, string>? Fields { get; init; }
}

/// <summary>Station headers (<c>icy-name</c>, <c>icy-genre</c> and so on) announced with the stream. NodeLink only.</summary>
public sealed record IcyStreamHeaders
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("genre")]
    public string? Genre { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>Sample rate as sent by the station, usually in kbit/s.</summary>
    [JsonPropertyName("bitrate")]
    public string? Bitrate { get; init; }
}

/// <summary>One live-stream metadata update, delivered through <c>StreamMetadataEvent</c>. NodeLink only.</summary>
public sealed record StreamMetadata
{
    [JsonPropertyName("metadata")]
    public IcyMetadata? Metadata { get; init; }

    /// <summary>Station headers captured when the stream connection was opened.</summary>
    [JsonPropertyName("icy")]
    public IcyStreamHeaders? Icy { get; init; }

    /// <summary>Unix timestamp, in milliseconds.</summary>
    [JsonPropertyName("receivedAt")]
    public long ReceivedAtMilliseconds { get; init; }
}
