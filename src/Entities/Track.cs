using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nyra.Exceptions;
using Nyra.Internal;

namespace Nyra.Entities;

/// <summary>Why a track ended.</summary>
/// <remarks>
/// The first five values are Lavalink's, the next two are NodeLink's, and the last is Nyra's fallback for a
/// reason it does not recognize.
/// </remarks>
public enum TrackEndReason
{
    /// <summary>Finished playing, a next track may be started.</summary>
    Finished,

    /// <summary>Failed loading, a next track may be started.</summary>
    LoadFailed,

    /// <summary>The track was stopped, the client should not start the next track.</summary>
    Stopped,

    /// <summary>The track was replaced with another one, the client should not start the next track.</summary>
    Replaced,

    /// <summary>
    /// The player was destroyed or the session expired, the client should not start the next track.
    /// </summary>
    Cleanup,

    /// <summary>The track ended in a gapless transition, the server already advanced to the successor.</summary>
    Gapless,

    /// <summary>The track ended in a crossfade, the server already advanced to the successor.</summary>
    Crossfading,

    /// <summary>
    /// An end reason the library does not know, reported by a newer server, whether starting the next track is
    /// safe is unknown, treat it conservatively.
    /// </summary>
    Unknown,
}

/// <summary>An audio track, as returned by load results, player updates and playback events.</summary>
public sealed record Track
{
    /// <summary>
    /// Highest encoding version the decoder understands, v1 carries no URI, v2 adds it, v3 adds artwork URL
    /// and ISRC.
    /// </summary>
    public const int MaxDecodeVersion = 3;

    /// <summary>Base64-encoded track string, used to play it back.</summary>
    /// <remarks>Never null, a missing value reads as an empty string.</remarks>
    [JsonPropertyName("encoded"), JsonInclude]
    public string Encoded
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>The descriptive info of the track.</summary>
    /// <remarks>Never null, a missing value reads as an empty instance.</remarks>
    [JsonPropertyName("info"), JsonInclude]
    public TrackInfo Info
    {
        get;
        internal set => field = value ?? new();
    } = new();

    /// <summary>The plugin data the server attached to the track, raw JSON.</summary>
    [JsonPropertyName("pluginInfo"), JsonInclude]
    public JsonElement? PluginInfo { get; internal set; }

    /// <summary>Track data previously stored by a client through the update-player endpoint.</summary>
    [JsonPropertyName("userData"), JsonInclude]
    public JsonElement? UserData { get; internal set; }

    /// <summary>Start offset from the source link's <c>t</c>/<c>start</c> parameter, client-side only.</summary>
    /// <remarks>
    /// A play without <see cref="PlayOptions.StartPosition"/> starts from it and clears it once the track plays. A
    /// <see cref="PlayOptions.NoReplace"/> play over a playing track keeps it. Sharing one track between guilds
    /// clears it for all of them, so copy the track with <c>with</c> for each guild.
    /// </remarks>
    [JsonIgnore]
    public TimeSpan? StartPosition { get; internal set; }

    /// <summary>Indicates whether the tracks carry the same encoding, info and raw plugin data.</summary>
    /// <param name="other">The track to compare with.</param>
    /// <returns>Whether the tracks are equal, raw members compared by content.</returns>
    public bool Equals(Track? other) =>
        other is not null
        && Encoded == other.Encoded
        && Info.Equals(other.Info)
        && ContentEquality.JsonElementEquals(PluginInfo, other.PluginInfo)
        && ContentEquality.JsonElementEquals(UserData, other.UserData);

    /// <summary>Returns the hash code of the track.</summary>
    /// <returns>A hash of the encoding and info, the raw members and the start offset stay out of it.</returns>
    public override int GetHashCode() => HashCode.Combine(Encoded, Info);

    /// <summary>Returns the track as its info.</summary>
    /// <returns>The track's info.</returns>
    public override string ToString() => Info.ToString();

    /// <summary>Decodes a base64 track locally, without contacting the server.</summary>
    /// <remarks>
    /// Source-specific data is ignored. A track copied from a URL, where a plus became a space, still decodes.
    /// </remarks>
    /// <param name="encodedTrack">The base64 track to decode.</param>
    /// <returns>The decoded track, position included.</returns>
    /// <exception cref="ArgumentNullException">The track argument is null.</exception>
    /// <exception cref="InvalidTrackException">
    /// The track is blank (empty or whitespace), is not valid base64, has a message size out of bounds, is
    /// truncated, uses an unsupported version or is malformed.
    /// </exception>
    public static Track Decode(string encodedTrack)
    {
        ArgumentNullException.ThrowIfNull(encodedTrack);
        if (string.IsNullOrWhiteSpace(encodedTrack))
            throw new InvalidTrackException("The encoded track is null, empty or whitespace.");

        try
        {
            return DecodeCore(encodedTrack, spaceIsPlus: false);
        }
        catch (InvalidTrackException) when (encodedTrack.Contains(' '))
        {
            return DecodeCore(encodedTrack, spaceIsPlus: true);
        }
    }

    private static Track DecodeCore(string encodedTrack, bool spaceIsPlus)
    {
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(
                spaceIsPlus
                    ? encodedTrack.TrimStart().TrimEnd('\r', '\n', '\t').Replace(' ', '+')
                    : encodedTrack
            );
        }
        catch (FormatException exception)
        {
            throw new InvalidTrackException("The encoded track is not valid base64.", exception);
        }

        try
        {
            var headerReader = new JavaBinaryReader(raw);
            var header = headerReader.ReadInt32();
            var flags = (int)((uint)header >> 30);
            var messageSize = header & 0x3FFFFFFF;
            if (messageSize <= 0 || messageSize > raw.Length - 4)
                throw new InvalidTrackException(
                    $"The encoded track's message size {messageSize} is out of bounds for a raw of {raw.Length} bytes.",
                    byteOffset: 0,
                    payloadLength: raw.Length
                );

            var message = raw.AsSpan(4, messageSize);
            var reader = new JavaBinaryReader(message);

            var canonicalEncoded = Convert.ToBase64String(raw);

            var version = (flags & 0b1) != 0 ? reader.ReadByte() : 1;
            if (version is < 1 or > MaxDecodeVersion)
                throw new InvalidTrackException(
                    $"The encoded track uses unsupported version {version} (supported: 1-{MaxDecodeVersion})."
                );

            var title = reader.ReadModifiedUtf8();
            var author = reader.ReadModifiedUtf8();
            var length = reader.ReadInt64();
            var identifier = reader.ReadModifiedUtf8();
            var isStream = reader.ReadBoolean();

            string? uri = null;
            string? artworkUrl = null;
            string? isrc = null;
            if (version >= 2)
                uri = reader.ReadNullableModifiedUtf8();
            if (version >= 3)
            {
                artworkUrl = reader.ReadNullableModifiedUtf8();
                isrc = reader.ReadNullableModifiedUtf8();
            }

            var sourceName = reader.ReadModifiedUtf8();

            if (reader.Remaining < 8)
                throw new InvalidTrackException(
                    $"The encoded track is truncated: {reader.Remaining} bytes remain of the delimited message, 8 are required.",
                    byteOffset: 4 + message.Length - reader.Remaining,
                    payloadLength: raw.Length
                );
            var position = BinaryPrimitives.ReadInt64BigEndian(message[^8..]);

            return new Track
            {
                Encoded = canonicalEncoded,
                Info = new TrackInfo
                {
                    Title = title,
                    Author = author,
                    LengthMilliseconds = length,
                    Identifier = identifier,
                    IsStream = isStream,
                    IsSeekable = !isStream,
                    Uri = uri,
                    ArtworkUrl = artworkUrl,
                    Isrc = isrc,
                    SourceName = sourceName,
                    PositionMilliseconds = position,
                },
            };
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidTrackException("The encoded track ended unexpectedly.", exception);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidTrackException(
                "The encoded track contains malformed data.",
                exception
            );
        }
    }
}

/// <summary>The descriptive info of a track.</summary>
/// <remarks>
/// A null the server sends for the identifier, author, title or source name reads as an empty string.
/// </remarks>
public sealed record TrackInfo
{
    /// <summary>The identifier of the track on its source, for example a video id.</summary>
    [JsonPropertyName("identifier"), JsonInclude]
    public string Identifier
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>The author of the track.</summary>
    [JsonPropertyName("author"), JsonInclude]
    public string Author
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Duration in milliseconds.</summary>
    /// <remarks>
    /// The raw value on a stream differs per server, <see cref="Length"/> is zero for all of them.
    /// </remarks>
    [JsonPropertyName("length"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long LengthMilliseconds { get; internal set; }

    /// <summary>The duration of the track, zero for streams.</summary>
    [JsonIgnore]
    public TimeSpan Length => ToTimeSpan(LengthMilliseconds);

    internal const long MaxTimeSpanMilliseconds = 922_337_203_685_477;

    internal static TimeSpan ToTimeSpan(long milliseconds) =>
        milliseconds is >= 0 and <= MaxTimeSpanMilliseconds
            ? TimeSpan.FromMilliseconds(milliseconds)
            : TimeSpan.Zero;

    /// <summary>Whether the track is a livestream.</summary>
    [JsonPropertyName("isStream"), JsonInclude]
    public bool IsStream { get; internal set; }

    /// <summary>Whether the track can be sought.</summary>
    [JsonPropertyName("isSeekable"), JsonInclude]
    public bool IsSeekable { get; internal set; }

    /// <summary>The saved position of the track, in milliseconds.</summary>
    [JsonPropertyName("position"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long PositionMilliseconds { get; internal set; }

    /// <summary>The saved position of the track.</summary>
    [JsonIgnore]
    public TimeSpan Position => ToTimeSpan(PositionMilliseconds);

    /// <summary>The title of the track.</summary>
    [JsonPropertyName("title"), JsonInclude]
    public string Title
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>The URI of the track on its source.</summary>
    [JsonPropertyName("uri"), JsonInclude]
    public string? Uri { get; internal set; }

    /// <summary>Source that produced the track, for example youtube.</summary>
    [JsonPropertyName("sourceName"), JsonInclude]
    public string SourceName
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>The artwork of the track.</summary>
    [JsonPropertyName("artworkUrl"), JsonInclude]
    public string? ArtworkUrl { get; internal set; }

    /// <summary>The ISRC of the track.</summary>
    [JsonPropertyName("isrc"), JsonInclude]
    public string? Isrc { get; internal set; }

    /// <summary>Returns the track as its source, title and author.</summary>
    /// <returns>The track's source, title and author.</returns>
    public override string ToString() => $"[{SourceName}] {Title} by {Author}";
}

internal sealed record TrackEncodeRequest
{
    [JsonPropertyName("identifier"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Identifier { get; internal set; } = string.Empty;

    [JsonPropertyName("author"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Author { get; internal set; } = string.Empty;

    [JsonPropertyName("length"), JsonInclude]
    public long Length { get; internal set; }

    [JsonPropertyName("isStream"), JsonInclude]
    public bool IsStream { get; internal set; }

    [JsonPropertyName("isSeekable"), JsonInclude]
    public bool IsSeekable { get; internal set; }

    [JsonPropertyName("position"), JsonInclude]
    public long Position { get; internal set; }

    [JsonPropertyName("title"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string Title { get; internal set; } = string.Empty;

    [JsonPropertyName("uri"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Uri { get; init; }

    [JsonPropertyName("sourceName"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string SourceName { get; internal set; } = string.Empty;

    [JsonPropertyName("artworkUrl"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ArtworkUrl { get; init; }

    [JsonPropertyName("isrc"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Isrc { get; init; }

    [JsonPropertyName("details"), JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public IReadOnlyList<string?> Details { get; init; } = [];
}
