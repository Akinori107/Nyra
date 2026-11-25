using System.Globalization;
using Nyra.Entities;

namespace Nyra.Internal;

internal sealed class NodeQueries
{
    private readonly Node _node;

    internal NodeQueries(Node node) => _node = node;

    internal async Task<LoadResult> LoadTracksAsync(
        string query,
        SearchType searchType = SearchType.Auto,
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        query = TrimQueryPadding(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);

        if (
            query.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || query.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || query.StartsWith("ftts://", StringComparison.OrdinalIgnoreCase)
        )
        {
            var result = await _node
                .Rest.Tracks.LoadAsync(query, cancellationToken)
                .ConfigureAwait(false);
            AttachUrlStartPosition(result, query);
            return result;
        }

        var prefix = searchType switch
        {
            SearchType.Auto or SearchType.YouTube => "ytsearch:",
            SearchType.YouTubeMusic => "ytmsearch:",
            SearchType.SoundCloud => "scsearch:",
            SearchType.Spotify => RequireLavaSrc("spsearch:"),
            SearchType.SpotifyRecommendations => RequireLavaSrc("sprec:"),
            SearchType.Monochrome => RequireNodeLink("monochrome:"),
            SearchType.Gaana => RequireNodeLink("gnsearch:"),
            SearchType.Bandcamp => RequireNodeLink("bcsearch:"),
            SearchType.IHeartRadio => RequireNodeLink("ihsearch:"),
            SearchType.YouTubeRecommendations => RequireNodeLink("ytrec:"),
            SearchType.QobuzRecommendations => RequireLavaSrc("qbrec:"),
            SearchType.LetrasRecommendations => RequireNodeLink("lmrec:"),
            SearchType.EternalBox => RequireNodeLink("eternalbox:"),
            SearchType.JioSaavn => RequireLavaSrc("jssearch:"),
            SearchType.JioSaavnRecommendations => RequireLavaSrc("jsrec:"),
            SearchType.Qobuz => RequireLavaSrc("qbsearch:"),
            SearchType.Tidal => RequireLavaSrc("tdsearch:"),
            SearchType.TidalRecommendations => RequireLavaSrc("tdrec:"),
            SearchType.Letras => RequireNodeLink("lmsearch:"),
            SearchType.Audius => RequireNodeLink("ausearch:"),
            SearchType.VkMusic => RequireLavaSrc("vksearch:"),
            SearchType.VkMusicRecommendations => RequireLavaSrc("vkrec:"),
            SearchType.AmazonMusic => RequireNodeLink("amazonmusic:"),
            SearchType.Bluesky => RequireNodeLink("bksearch:"),
            SearchType.AppleMusic => RequireLavaSrc("amsearch:"),
            SearchType.NetEase => RequireNodeLink("ntsearch:"),
            SearchType.YandexMusic => RequireLavaSrc("ymsearch:"),
            SearchType.YandexMusicRecommendations => RequireLavaSrc("ymrec:"),
            SearchType.Bilibili => RequireNodeLink("bilisearch:"),
            SearchType.LastFm => RequireNodeLink("lfsearch:"),
            SearchType.GoogleDrive => RequireNodeLink("gdsearch:"),
            SearchType.Shazam => RequireNodeLink("shsearch:"),
            SearchType.Deezer => RequireLavaSrc("dzsearch:"),
            SearchType.DeezerRecommendations => RequireLavaSrc("dzrec:"),
            SearchType.Anghami => RequireNodeLink("agsearch:"),
            SearchType.Audiomack => RequireNodeLink("admsearch:"),
            SearchType.Pandora => RequireNodeLink("pdsearch:"),
            SearchType.NicoVideo => RequireNodeLink("ncsearch:"),
            SearchType.SongLink => RequireNodeLink("slsearch:"),
            SearchType.Flowery => RequireNodeLink("ftts:"),
            SearchType.GoogleTts => RequireNodeLink("gtts:"),
            SearchType.PiperTts => RequireNodeLink("pipertts:"),
            SearchType.LazyPyTts => RequireNodeLink("lazypytts:"),
            SearchType.Raw => string.Empty,
            _ => throw new ArgumentOutOfRangeException(
                nameof(searchType),
                searchType,
                "Unknown search type."
            ),
        };

        var identifier = prefix + query;

        return await _node
            .Rest.Tracks.LoadAsync(identifier, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string TrimQueryPadding(string query)
    {
        var start = 0;
        var end = query.Length;
        while (start < end && IsQueryPadding(query[start]))
            start++;
        while (end > start && IsQueryPadding(query[end - 1]))
            end--;

        return start == 0 && end == query.Length ? query : query[start..end];
    }

    private static bool IsQueryPadding(char character) =>
        char.IsWhiteSpace(character)
        || char.GetUnicodeCategory(character) == UnicodeCategory.Format;

    private string RequireLavaSrc(string prefix)
    {
        NodeGuards.RequireNodeLinkOrPlugin(_node, NodeGuards.LavaSrcPlugin, "this search source");
        return prefix;
    }

    private string RequireNodeLink(string prefix)
    {
        NodeGuards.RequireNodeLink(_node, "this search source");
        return prefix;
    }

    internal async Task<IReadOnlyList<Player>> ListPlayersAsync(
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);

        return await _node.Rest.Players.ListAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<NodeInfo> GetNodeInfoAsync(CancellationToken cancellationToken = default)
    {
        _node.ThrowIfDisposed();
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);

        return await _node.Rest.Info.GetNodeInfoAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<Stats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        _node.ThrowIfDisposed();
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);

        return await _node.Rest.Info.GetStatsAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<ConnectionStatus> GetConnectionStatusAsync(
        CancellationToken cancellationToken = default
    )
    {
        _node.ThrowIfDisposed();
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        NodeGuards.RequireNodeLink(_node, "connection status");

        return await _node
            .Rest.Info.GetConnectionStatusAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static void AttachUrlStartPosition(LoadResult result, string query)
    {
        if (UrlStartPosition.ParseMilliseconds(query) is not { } milliseconds)
            return;

        var offset = TimeSpan.FromMilliseconds(milliseconds);

        switch (result)
        {
            case TrackLoaded loaded:
                loaded.Track.StartPosition = offset;
                break;

            case SearchLoaded search:
                foreach (var track in search.Tracks)
                    track.StartPosition = offset;
                break;

            case PlaylistLoaded playlist:
                var index = playlist.PlaylistInfo.SelectedTrack;
                if (index >= 0 && index < playlist.Tracks.Count)
                    playlist.Tracks[index].StartPosition = offset;
                break;
        }
    }

    internal async Task<NodeVersion?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        _node.ThrowIfDisposed();
        await _node.EnsureServerInfoAsync(cancellationToken).ConfigureAwait(false);
        return _node.Version;
    }
}
