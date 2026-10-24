namespace Nyra.Internal;

internal static class Routes
{
    public const string WebSocketPath = "/v4/websocket";

    public const string Info = "/v4/info";

    public const string LoadTracks = "/v4/loadtracks";

    public const string LoadLyrics = "/v4/loadlyrics";

    public const string Lyrics = "/v4/lyrics";

    public const string LoadChapters = "/v4/loadchapters";

    public const string DecodeTrack = "/v4/decodetrack";

    public const string DecodeTracks = "/v4/decodetracks";

    public const string EncodedTracks = "/v4/encodedtracks";

    public const string Session = "/v4/sessions/{0}";

    public const string Players = "/v4/sessions/{0}/players";

    public const string Stats = "/v4/stats";

    public const string Connection = "/v4/connection";

    public const string Meaning = "/v4/meaning";

    public const string RoutePlannerStatus = "/v4/routeplanner/status";

    public const string RoutePlannerFreeAddress = "/v4/routeplanner/free/address";

    public const string RoutePlannerFreeAll = "/v4/routeplanner/free/all";

    public const string Player = "/v4/sessions/{0}/players/{1}";

    public const string LyricsSubscription = "/v4/sessions/{0}/players/{1}/lyrics/subscribe";

    public const string SponsorBlock = "/v4/sessions/{0}/players/{1}/sponsorblock";

    public const string Mix = "/v4/sessions/{0}/players/{1}/mix";

    public const string MixLayer = "/v4/sessions/{0}/players/{1}/mix/{2}";

    public const string Groups = "/v4/sessions/{0}/groups";

    public const string Group = "/v4/sessions/{0}/groups/{1}";

    public const string TrackStream = "/v4/trackstream";

    public const string LoadStream = "/v4/loadstream";

    public const string YouTubeConfig = "/v4/youtube/config";

    public const string YouTubeOAuth = "/v4/youtube/oauth";
}
