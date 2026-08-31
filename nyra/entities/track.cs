using System.Buffers.Binary;
using System.Text.Json.Serialization;
using Nyra.Exceptions;
using Nyra.Internal;

namespace Nyra.Entities;

/// <summary>Strategy used to resolve a track query on <see cref="Node.LoadTracksAsync"/>.
/// Direct http(s) URLs are always sent as-is regardless of the search type, since prefixing a
/// URL is never valid.</summary>
public enum SearchType
{
    /// <summary>Sends URLs raw and everything else to <c>ytsearch:</c>; the default.</summary>
    Auto,

    /// <summary>Prefixes the query with <c>ytsearch:</c>.</summary>
    [JsonPropertyName("ytsearch")]
    YouTube,

    /// <summary>Prefixes the query with <c>ytmsearch:</c>.</summary>
    YouTubeMusic,

    /// <summary>Prefixes the query with <c>scsearch:</c>.</summary>
    SoundCloud,

    /// <summary>Sends the query as-is: direct URLs, or identifiers that already carry a prefix.</summary>
    Raw,
}

/// <summary>
/// Outcome of a load-tracks operation. NodeLink reports extra types (episode, album, artist,
/// station, podcast, show); Nyra maps them onto <see cref="Track"/> and <see cref="Playlist"/>.
/// </summary>
public enum LoadType
{
    [JsonPropertyName("track")]
    Track,

    [JsonPropertyName("playlist")]
    Playlist,

    [JsonPropertyName("search")]
    Search,

    [JsonPropertyName("empty")]
    Empty,

    [JsonPropertyName("error")]
    Error,
}

/// <summary>Why a track ended. The first five values are Lavalink's; the last two are NodeLink's.</summary>
public enum TrackEndReason
{
    /// <summary>Finished playing; a next track may be started.</summary>
    [JsonPropertyName("finished")]
    Finished,

    /// <summary>Failed loading; a next track may be started.</summary>
    [JsonPropertyName("loadFailed")]
    LoadFailed,

    [JsonPropertyName("stopped")]
    Stopped,

    [JsonPropertyName("replaced")]
    Replaced,

    /// <summary>The player was destroyed or the session expired.</summary>
    [JsonPropertyName("cleanup")]
    Cleanup,

    [JsonPropertyName("gapless")]
    Gapless,

    [JsonPropertyName("crossfading")]
    Crossfading,

    /// <summary>An end reason the library does not know, reported by a newer server.</summary>
    [JsonPropertyName("unknown")]
    Unknown,
}

public enum ErrorSeverity
{
    /// <summary>The cause is known and expected.</summary>
    [JsonPropertyName("common")]
    Common,

    /// <summary>Possibly caused by outside factors, such as an unexpected upstream response.</summary>
    [JsonPropertyName("suspicious")]
    Suspicious,

    /// <summary>The server itself is probably at fault, or the cause cannot be determined.</summary>
    [JsonPropertyName("fault")]
    Fault,

    /// <summary>A severity the library does not know, reported by a newer server.</summary>
    [JsonPropertyName("unknown")]
    Unknown,
}

/// <summary>An error reported by an audio node, in load results and track exception events.</summary>
public sealed record ErrorInfo
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>May be omitted by NodeLink.</summary>
    [JsonPropertyName("severity")]
    public ErrorSeverity? Severity { get; init; }

    [JsonPropertyName("cause")]
    public string? Cause { get; init; }

    /// <summary>Lavalink only, since v4.1.0.</summary>
    [JsonPropertyName("causeStackTrace")]
    public string? CauseStackTrace { get; init; }

    public override string ToString() =>
        $"[{Severity?.ToString() ?? "unknown"}] {Message ?? Cause ?? "no message"}";
}

public sealed record PlaylistInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Zero-based index of the selected track, or -1 when none is selected.</summary>
    [JsonPropertyName("selectedTrack")]
    public int SelectedTrack { get; init; } = -1;
}

/// <summary>An audio track, as returned by load results, player updates and playback events.</summary>
public sealed record Track
{
    /// <summary>Highest encoding version the decoder understands: v1 carries no URI, v2 adds it, v3 adds artwork URL and ISRC.</summary>
    public const int MaxDecodeVersion = 3;

    /// <summary>Base64-encoded track string, used to play it back.</summary>
    [JsonPropertyName("encoded")]
    public string Encoded { get; init; } = string.Empty;

    [JsonPropertyName("info")]
    public TrackInfo Info { get; init; } = new();

    [JsonPropertyName("pluginInfo")]
    public System.Text.Json.JsonElement? PluginInfo { get; init; }

    /// <summary>Track data previously stored by a client through the update-player endpoint.</summary>
    [JsonPropertyName("userData")]
    public System.Text.Json.JsonElement? UserData { get; init; }

    /// <summary>
    /// Start offset from the source link's <c>t</c>/<c>start</c> parameter, client-side only.
    /// Applied on the track's first <see cref="GuildConnection.PlayAsync"/> unless an explicit
    /// <see cref="PlayOptions.StartPosition"/> is given; consumed after that first play.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? StartPosition { get; internal set; }

    public override string ToString() => Info.ToString();

    /// <summary>
    /// Decodes a base64 track locally, without contacting the server. The position is read
    /// from the last 8 bytes of the payload, so source-specific data written before it is
    /// safely ignored.
    /// </summary>
    /// <exception cref="InvalidTrackException">The track is not valid base64, is truncated or uses an unsupported version.</exception>
    public static Track Decode(string encodedTrack)
    {
        if (string.IsNullOrWhiteSpace(encodedTrack))
            throw new InvalidTrackException("The encoded track is empty.");

        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(encodedTrack);
        }
        catch (FormatException exception)
        {
            throw new InvalidTrackException("The encoded track is not valid base64.", exception);
        }

        var payload = TrimLegacyNodeLinkTrailer(raw);

        try
        {
            var reader = new JavaBinaryReader(payload);

            var header = reader.ReadInt32();
            var flags = (int)((uint)header >> 30);
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

            // Everything between the source name and the final 8 bytes is source-specific
            // payload (Lavalink) or track details (NodeLink); the position is always the
            // last 8 bytes, big-endian.
            if (reader.Remaining < 8)
                throw new InvalidTrackException("The encoded track is truncated.");
            var position = BinaryPrimitives.ReadInt64BigEndian(payload[^8..]);

            return new Track
            {
                Encoded = encodedTrack,
                Info = new TrackInfo
                {
                    Title = title,
                    Author = author,
                    LengthMilliseconds = length,
                    Identifier = identifier,
                    IsStream = isStream,
                    // The binary format carries no seekability; derive it from the stream flag, like NodeLink.
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

    /// <summary>
    /// Removes the legacy 16-byte <c>NLK:seekable</c> trailer that old NodeLink versions wrote
    /// after the position: <c>[present=0x01][utflen=0x000D]["NLK:seekable" + Y|N]</c>. The full
    /// structure is validated — not just the marker — so tracks that merely contain the marker
    /// bytes in their payload are left untouched.
    /// </summary>
    private static ReadOnlySpan<byte> TrimLegacyNodeLinkTrailer(ReadOnlySpan<byte> raw)
    {
        ReadOnlySpan<byte> marker = "NLK:seekable"u8;

        // The trailer always sits at the very end: 12 marker bytes plus the 1-byte seekable flag.
        if (raw.Length < 16 || raw[^1] is not ((byte)'Y') and not ((byte)'N'))
            return raw;

        if (!raw.Slice(raw.Length - 13, marker.Length).SequenceEqual(marker))
            return raw;

        // [present=true][utflen=13] right before the marker.
        if (raw[^16] != 1 || raw[^15] != 0 || raw[^14] != 13)
            return raw;

        return raw[..^16];
    }
}

public sealed record TrackInfo
{
    [JsonPropertyName("identifier")]
    public string Identifier { get; init; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    /// <summary>
    /// Duration in milliseconds. Lavalink reports <see cref="long.MaxValue"/> for streams and
    /// NodeLink reports 0; <see cref="Length"/> returns zero for both.
    /// </summary>
    [JsonPropertyName("length")]
    public long LengthMilliseconds { get; init; }

    [JsonIgnore]
    public TimeSpan Length =>
        LengthMilliseconds is > 0 and <= MaxTimeSpanMilliseconds
            ? TimeSpan.FromMilliseconds(LengthMilliseconds)
            : TimeSpan.Zero;

    /// <summary>Largest millisecond value <see cref="TimeSpan"/> can hold; anything above it is a stream marker.</summary>
    internal const long MaxTimeSpanMilliseconds = 922_337_203_685_477;

    [JsonPropertyName("isStream")]
    public bool IsStream { get; init; }

    [JsonPropertyName("isSeekable")]
    public bool IsSeekable { get; init; }

    [JsonPropertyName("position")]
    public long PositionMilliseconds { get; init; }

    [JsonIgnore]
    public TimeSpan Position =>
        PositionMilliseconds is >= 0 and <= MaxTimeSpanMilliseconds
            ? TimeSpan.FromMilliseconds(PositionMilliseconds)
            : TimeSpan.Zero;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("uri")]
    public string? Uri { get; init; }

    /// <summary>Source that produced the track, for example <c>youtube</c>.</summary>
    [JsonPropertyName("sourceName")]
    public string SourceName { get; init; } = string.Empty;

    [JsonPropertyName("artworkUrl")]
    public string? ArtworkUrl { get; init; }

    [JsonPropertyName("isrc")]
    public string? Isrc { get; init; }

    public override string ToString() => $"[{SourceName}] {Title} by {Author}";
}

/// <summary>Result of a load-tracks operation; pattern-match on the derived record type.</summary>
public abstract record LoadResult
{
    public abstract LoadType LoadType { get; }

    public sealed record TrackLoaded(Track Track) : LoadResult
    {
        public override LoadType LoadType => LoadType.Track;
    }

    public sealed record PlaylistLoaded(
        PlaylistInfo PlaylistInfo,
        IReadOnlyList<Track> Tracks,
        System.Text.Json.JsonElement? PluginInfo = null
    ) : LoadResult
    {
        public override LoadType LoadType => LoadType.Playlist;
    }

    public sealed record SearchLoaded(IReadOnlyList<Track> Tracks) : LoadResult
    {
        public override LoadType LoadType => LoadType.Search;
    }

    public sealed record NoMatches : LoadResult
    {
        public override LoadType LoadType => LoadType.Empty;
    }

    public sealed record LoadFailed(ErrorInfo Error) : LoadResult
    {
        public override LoadType LoadType => LoadType.Error;
    }
}
