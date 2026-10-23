using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Strategy for resolving a track query on <see cref="Node.LoadTracksAsync"/>.</summary>
/// <remarks>
/// Direct http(s) and <c>ftts</c> URLs are always sent as-is, since prefixing a URL is never valid.
/// </remarks>
public enum SearchType
{
    /// <summary>Sends URLs raw and everything else to <c>ytsearch</c>, the default.</summary>
    Auto,

    /// <summary>Prefixes the query with <c>ytsearch</c>.</summary>
    YouTube,

    /// <summary>Prefixes the query with <c>ytmsearch</c>.</summary>
    YouTubeMusic,

    /// <summary>Prefixes the query with <c>scsearch</c>.</summary>
    SoundCloud,

    /// <summary>Sends the query as-is, direct URLs, or identifiers that already carry a prefix.</summary>
    Raw,

    /// <summary>Prefixes the query with <c>spsearch</c>, searching Spotify.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    Spotify,

    /// <summary>Prefixes the query with <c>sprec</c>.</summary>
    /// <remarks>
    /// NodeLink, or Lavalink with the LavaSrc plugin. NodeLink's Audius source registers the same prefix, so
    /// with both enabled either may answer.
    /// </remarks>
    SpotifyRecommendations,

    /// <summary>
    /// Prefixes the query with <c>monochrome</c> to reach the Monochrome source by name.
    /// </summary>
    /// <remarks>NodeLink only, it fails when Monochrome is disabled.</remarks>
    Monochrome,

    /// <summary>Prefixes the query with <c>gnsearch</c>, searching Gaana.</summary>
    /// <remarks>NodeLink only.</remarks>
    Gaana,

    /// <summary>Prefixes the query with <c>bcsearch</c>, searching Bandcamp.</summary>
    /// <remarks>NodeLink only.</remarks>
    Bandcamp,

    /// <summary>Prefixes the query with <c>ihsearch</c>, searching iHeartRadio.</summary>
    /// <remarks>NodeLink only.</remarks>
    IHeartRadio,

    /// <summary>Prefixes the query with <c>ytrec</c>, loading YouTube recommendations for the query.</summary>
    /// <remarks>NodeLink only.</remarks>
    YouTubeRecommendations,

    /// <summary>Prefixes the query with <c>qbrec</c>, loading Qobuz recommendations.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    QobuzRecommendations,

    /// <summary>Prefixes the query with <c>lmrec</c>, loading Letras recommendations.</summary>
    /// <remarks>NodeLink only.</remarks>
    LetrasRecommendations,

    /// <summary>Prefixes the query with <c>eternalbox</c>, loading an EternalBox continuous-playback track.</summary>
    /// <remarks>NodeLink only.</remarks>
    EternalBox,

    /// <summary>Prefixes the query with <c>jssearch</c>, searching JioSaavn.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    JioSaavn,

    /// <summary>Prefixes the query with <c>jsrec</c>, loading JioSaavn recommendations.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    JioSaavnRecommendations,

    /// <summary>Prefixes the query with <c>qbsearch</c>, searching Qobuz.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    Qobuz,

    /// <summary>Prefixes the query with <c>tdsearch</c>, searching Tidal.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    Tidal,

    /// <summary>Prefixes the query with <c>tdrec</c>, loading Tidal recommendations.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    TidalRecommendations,

    /// <summary>Prefixes the query with <c>lmsearch</c>, searching Letras.</summary>
    /// <remarks>NodeLink only.</remarks>
    Letras,

    /// <summary>Prefixes the query with <c>ausearch</c>, searching Audius.</summary>
    /// <remarks>NodeLink only.</remarks>
    Audius,

    /// <summary>Prefixes the query with <c>vksearch</c>, searching VK Music.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    VkMusic,

    /// <summary>Prefixes the query with <c>vkrec</c>, loading VK Music recommendations.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    VkMusicRecommendations,

    /// <summary>Prefixes the query with <c>amazonmusic</c>, searching Amazon Music.</summary>
    /// <remarks>NodeLink only.</remarks>
    AmazonMusic,

    /// <summary>Prefixes the query with <c>bksearch</c>, searching Bluesky.</summary>
    /// <remarks>NodeLink only.</remarks>
    Bluesky,

    /// <summary>Prefixes the query with <c>amsearch</c>, searching Apple Music.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    AppleMusic,

    /// <summary>Prefixes the query with <c>ntsearch</c>, searching NetEase Cloud Music.</summary>
    /// <remarks>NodeLink only.</remarks>
    NetEase,

    /// <summary>Prefixes the query with <c>ymsearch</c>, searching Yandex Music.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    YandexMusic,

    /// <summary>Prefixes the query with <c>ymrec</c>, loading Yandex Music recommendations.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    YandexMusicRecommendations,

    /// <summary>Prefixes the query with <c>bilisearch</c>, searching Bilibili.</summary>
    /// <remarks>NodeLink only.</remarks>
    Bilibili,

    /// <summary>Prefixes the query with <c>lfsearch</c>, searching Last.fm.</summary>
    /// <remarks>NodeLink only.</remarks>
    LastFm,

    /// <summary>Prefixes the query with <c>gdsearch</c>, searching a configured Google Drive folder.</summary>
    /// <remarks>NodeLink only.</remarks>
    GoogleDrive,

    /// <summary>Prefixes the query with <c>shsearch</c>, searching Shazam.</summary>
    /// <remarks>NodeLink only.</remarks>
    Shazam,

    /// <summary>Prefixes the query with <c>dzsearch</c>, searching Deezer.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    Deezer,

    /// <summary>Prefixes the query with <c>dzrec</c>, loading Deezer recommendations.</summary>
    /// <remarks>NodeLink, or Lavalink with the LavaSrc plugin.</remarks>
    DeezerRecommendations,

    /// <summary>Prefixes the query with <c>agsearch</c>, searching Anghami.</summary>
    /// <remarks>NodeLink only.</remarks>
    Anghami,

    /// <summary>Prefixes the query with <c>admsearch</c>, searching Audiomack.</summary>
    /// <remarks>NodeLink only.</remarks>
    Audiomack,

    /// <summary>Prefixes the query with <c>pdsearch</c>, searching Pandora.</summary>
    /// <remarks>NodeLink only.</remarks>
    Pandora,

    /// <summary>Prefixes the query with <c>ncsearch</c>, searching Niconico (nicovideo).</summary>
    /// <remarks>NodeLink only.</remarks>
    NicoVideo,

    /// <summary>Prefixes the query with <c>slsearch</c>, searching Song.link's iTunes-backed catalog.</summary>
    /// <remarks>NodeLink only.</remarks>
    SongLink,

    /// <summary>Prefixes the query with <c>ftts</c>, synthesizing speech through Flowery TTS.</summary>
    /// <remarks>
    /// NodeLink only. LavaSrc matches the <c>ftts</c> URL form instead, which is sent as-is like any URL.
    /// </remarks>
    Flowery,

    /// <summary>Prefixes the query with <c>gtts</c>, synthesizing speech through Google Translate TTS.</summary>
    /// <remarks>NodeLink only.</remarks>
    GoogleTts,

    /// <summary>Prefixes the query with <c>pipertts</c>, synthesizing speech through Piper TTS.</summary>
    /// <remarks>NodeLink only.</remarks>
    PiperTts,

    /// <summary>Prefixes the query with <c>lazypytts</c>, synthesizing speech through LazyPyTTS.</summary>
    /// <remarks>NodeLink only.</remarks>
    LazyPyTts,
}

/// <summary>Outcome of a load-tracks operation.</summary>
/// <remarks>
/// NodeLink's extra types (episode, album, artist, station, podcast, show) map onto <see cref="Track"/> and
/// <see cref="Playlist"/>.
/// </remarks>
public enum LoadType
{
    /// <summary>The load returned one track.</summary>
    Track,

    /// <summary>The load returned a playlist.</summary>
    Playlist,

    /// <summary>The load returned search results.</summary>
    Search,

    /// <summary>The load matched nothing.</summary>
    Empty,

    /// <summary>The load failed.</summary>
    Error,

    /// <summary>Not a server value.</summary>
    /// <remarks>
    /// Nyra's fallback for a load type the library does not recognize. The load path itself degrades unknown
    /// types into <see cref="LoadResult.LoadFailed"/> instead, this member only exists so a future payload
    /// that deserializes the enum directly degrades the same way instead of throwing mid-parse.
    /// </remarks>
    Unknown,
}

/// <summary>How severe the server considers a load error.</summary>
public enum ErrorSeverity
{
    /// <summary>The cause is known and expected.</summary>
    Common,

    /// <summary>Possibly caused by outside factors, such as an unexpected upstream response.</summary>
    Suspicious,

    /// <summary>The failure is the server's own, or the cause cannot be determined.</summary>
    Fault,

    /// <summary>A severity the library does not know, reported by a newer server.</summary>
    Unknown,
}

/// <summary>An error reported by an audio node, in load results and track exception events.</summary>
public sealed record ErrorInfo
{
    /// <summary>The message of the error.</summary>
    [JsonPropertyName("message"), JsonInclude]
    public string? Message { get; internal set; }

    /// <summary>May be omitted by NodeLink.</summary>
    [JsonPropertyName("severity"), JsonInclude]
    public ErrorSeverity? Severity { get; internal set; }

    /// <summary>The cause of the error.</summary>
    [JsonPropertyName("cause"), JsonInclude]
    public string? Cause { get; internal set; }

    /// <summary>Lavalink only.</summary>
    [JsonPropertyName("causeStackTrace"), JsonInclude]
    public string? CauseStackTrace { get; internal set; }

    /// <summary>Returns the error as severity and message, or the cause when there is no message.</summary>
    /// <returns>The formatted error, from whatever parts it has.</returns>
    public override string ToString() =>
        $"[{Severity?.ToString() ?? "unknown"}] {Message ?? Cause ?? "no message"}";
}

/// <summary>The metadata of a loaded playlist.</summary>
public sealed record PlaylistInfo
{
    /// <summary>The name of the playlist.</summary>
    [JsonPropertyName("name"), JsonInclude]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>Zero-based index of the selected track, or -1 when none is selected.</summary>
    /// <remarks>
    /// The index is whatever the server reported, no bounds against the playlist's track count are guaranteed,
    /// guard the upper bound before indexing with it.
    /// </remarks>
    [JsonPropertyName("selectedTrack"), JsonInclude]
    public int SelectedTrack { get; internal set; } = -1;
}

/// <summary>Result of a load-tracks operation, pattern-match on the derived record type.</summary>
public abstract record LoadResult
{
    /// <summary>The outcome of the load.</summary>
    public abstract LoadType LoadType { get; }

    /// <summary>One track was loaded.</summary>
    /// <param name="Track">The track that was loaded.</param>
    public sealed record TrackLoaded(Track Track) : LoadResult
    {
        /// <summary>The load type of the result, always Track.</summary>
        public override LoadType LoadType => LoadType.Track;
    }

    /// <summary>A playlist was loaded.</summary>
    /// <param name="PlaylistInfo">The metadata of the playlist.</param>
    /// <param name="Tracks">The tracks of the playlist.</param>
    /// <param name="PluginInfo">The plugin data the server attached to the playlist, raw JSON.</param>
    public sealed record PlaylistLoaded(
        PlaylistInfo PlaylistInfo,
        IReadOnlyList<Track> Tracks,
        JsonElement? PluginInfo = null
    ) : LoadResult
    {
        /// <summary>The load type of the result, always Playlist.</summary>
        public override LoadType LoadType => LoadType.Playlist;

        /// <summary>Indicates whether the loads carry the same members.</summary>
        /// <param name="other">The load to compare with.</param>
        /// <returns>Whether the members match, the tracks compared by content.</returns>
        public bool Equals(PlaylistLoaded? other) =>
            other is not null
            && PlaylistInfo.Equals(other.PlaylistInfo)
            && ContentEquality.ListEquals(Tracks, other.Tracks)
            && ContentEquality.JsonElementEquals(PluginInfo, other.PluginInfo);

        /// <summary>Returns a hash of the load's comparable members.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => HashCode.Combine(PlaylistInfo, Tracks?.Count ?? 0);
    }

    /// <summary>Search results were loaded.</summary>
    /// <param name="Tracks">The tracks the search found.</param>
    public sealed record SearchLoaded(IReadOnlyList<Track> Tracks) : LoadResult
    {
        /// <summary>The load type of the result, always Search.</summary>
        public override LoadType LoadType => LoadType.Search;

        /// <summary>Indicates whether the loads carry the same tracks.</summary>
        /// <param name="other">The load to compare with.</param>
        /// <returns>Whether the tracks match by content.</returns>
        public bool Equals(SearchLoaded? other) =>
            other is not null && ContentEquality.ListEquals(Tracks, other.Tracks);

        /// <summary>Returns a hash of the load's comparable members.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode() => HashCode.Combine(Tracks?.Count ?? 0);
    }

    /// <summary>The load matched nothing.</summary>
    public sealed record NoMatches : LoadResult
    {
        /// <summary>The load type of the result, always Empty.</summary>
        public override LoadType LoadType => LoadType.Empty;
    }

    /// <summary>The load failed.</summary>
    /// <param name="Error">The error the load failed with.</param>
    public sealed record LoadFailed(ErrorInfo Error) : LoadResult
    {
        /// <summary>The load type of the result, always Error.</summary>
        public override LoadType LoadType => LoadType.Error;
    }
}
