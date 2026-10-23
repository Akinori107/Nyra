using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Shoutcast/Icecast metadata of a live stream, reported while the stream plays.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed record IcyMetadata
{
    /// <summary>Raw metadata block, with ICY NUL padding and surrounding whitespace stripped by the server.</summary>
    [JsonPropertyName("raw"), JsonInclude]
    public string? Raw { get; internal set; }

    /// <summary>The title the stream reports.</summary>
    [JsonPropertyName("streamTitle"), JsonInclude]
    public string? StreamTitle { get; internal set; }

    /// <summary>The URL the stream reports.</summary>
    [JsonPropertyName("streamUrl"), JsonInclude]
    public string? StreamUrl { get; internal set; }

    /// <summary>
    /// The key='value' pairs the server's parser recognizes, alphanumeric keys with apostrophe-delimited
    /// values, keys lowercased by the server, null values dropped by this client. Raw carries the
    /// untouched block for anything the parser misses.
    /// </summary>
    [JsonPropertyName("fields"), JsonInclude]
    public IReadOnlyDictionary<string, string>? Fields
    {
        get;
        internal set => field = value?.Where(pair => pair.Value is not null).ToDictionary();
    }

    /// <summary>Returns the metadata with its normalized field count.</summary>
    /// <returns>The public members, the raw backing field stays out of it.</returns>
    public override string ToString() =>
        $"IcyMetadata {{ StreamTitle = {StreamTitle ?? "null"}, StreamUrl = {StreamUrl ?? "null"}, Fields = {Fields?.Count ?? 0} pairs }}";

    /// <summary>Indicates whether the metadata carries the same members.</summary>
    /// <param name="other">The metadata to compare with.</param>
    /// <returns>Whether the members match, the fields compared by content.</returns>
    public bool Equals(IcyMetadata? other) =>
        other is not null
        && Raw == other.Raw
        && StreamTitle == other.StreamTitle
        && StreamUrl == other.StreamUrl
        && ContentEquality.DictionaryEquals(Fields, other.Fields);

    /// <summary>Returns a hash of the metadata's comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Raw, StreamTitle, StreamUrl);
}

/// <summary>Station headers announced with the stream.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed record IcyStreamHeaders
{
    /// <summary>The name the stream reports.</summary>
    [JsonPropertyName("name"), JsonInclude]
    public string? Name { get; internal set; }

    /// <summary>The description the stream reports.</summary>
    [JsonPropertyName("description"), JsonInclude]
    public string? Description { get; internal set; }

    /// <summary>The genre the stream reports.</summary>
    [JsonPropertyName("genre"), JsonInclude]
    public string? Genre { get; internal set; }

    /// <summary>The URL the stream reports.</summary>
    [JsonPropertyName("url"), JsonInclude]
    public string? Url { get; internal set; }

    /// <summary>Bit rate as sent by the station, usually in kbit/s.</summary>
    [JsonPropertyName("bitrate"), JsonInclude]
    public string? Bitrate { get; internal set; }
}

/// <summary>One live-stream metadata update, delivered through <c>StreamMetadataEvent</c>.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed record StreamMetadata
{
    /// <summary>The ICY metadata of the stream.</summary>
    [JsonPropertyName("metadata"), JsonInclude]
    public IcyMetadata? Metadata { get; internal set; }

    /// <summary>Station headers captured when the stream connection was opened.</summary>
    [JsonPropertyName("icy"), JsonInclude]
    public IcyStreamHeaders? Icy { get; internal set; }

    /// <summary>Unix timestamp, in milliseconds, null when the server sent none.</summary>
    [JsonPropertyName("receivedAt"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? ReceivedAtMilliseconds { get; internal set; }

    /// <summary>When the server received the metadata, null when it sent no timestamp.</summary>
    /// <remarks>
    /// A value outside the <see cref="DateTimeOffset"/> range (a hostile host) reads as <see
    /// cref="DateTimeOffset.MinValue"/>, which the exact floor of the range reads as too, so the sentinel
    /// cannot be told apart from it.
    /// </remarks>
    [JsonIgnore]
    public DateTimeOffset? ReceivedAt =>
        ReceivedAtMilliseconds is { } milliseconds ? UnixTime.ToDateTimeOffset(milliseconds) : null;
}
