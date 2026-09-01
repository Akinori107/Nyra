namespace Nyra;

/// <summary>REST and websocket paths of the Lavalink v4 / NodeLink v3+ protocol.</summary>
internal static class Endpoints
{
    public const string WebSocketPath = "/v4/websocket";

    // Served by both servers; the JSON body carries the server kind (NodeLink adds
    // "isNodelink": true) and the version under "version.semver".
    public const string Info = "/v4/info";

    public const string LoadTracks = "/v4/loadtracks";

    // NodeLink only.
    public const string LoadLyrics = "/v4/loadlyrics";

    // Lavalink with the LavaLyrics plugin only.
    public const string Lyrics = "/v4/lyrics";

    public const string Session = "/v4/sessions/{0}";

    // {0} = session id, {1} = guild id.
    public const string Player = "/v4/sessions/{0}/players/{1}";

    // {0} = session id, {1} = guild id; NodeLink, or Lavalink with the LavaLyrics plugin.
    public const string LyricsSubscription = "/v4/sessions/{0}/players/{1}/lyrics/subscribe";

    // {0} = session id, {1} = guild id; NodeLink only.
    public const string SponsorBlock = "/v4/sessions/{0}/players/{1}/sponsorblock";
}
