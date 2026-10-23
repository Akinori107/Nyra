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

        query = TrimFormatCharacters(query.Trim());
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

        var identifier = searchType switch
        {
            SearchType.Auto or SearchType.YouTube => $"ytsearch:{query}",
            SearchType.YouTubeMusic => $"ytmsearch:{query}",
            SearchType.SoundCloud => $"scsearch:{query}",
            SearchType.Raw => query,
            SearchType.Spotify => $"spsearch:{query}",
            SearchType.SpotifyRecommendations => $"sprec:{query}",
            SearchType.Monochrome => $"monochrome:{query}",
            SearchType.Gaana => $"gnsearch:{query}",
            SearchType.Bandcamp => $"bcsearch:{query}",
            SearchType.IHeartRadio => $"ihsearch:{query}",
            SearchType.YouTubeRecommendations => $"ytrec:{query}",
            SearchType.QobuzRecommendations => $"qbrec:{query}",
            SearchType.LetrasRecommendations => $"lmrec:{query}",
            SearchType.EternalBox => $"eternalbox:{query}",
            SearchType.JioSaavn => $"jssearch:{query}",
            SearchType.JioSaavnRecommendations => $"jsrec:{query}",
            SearchType.Qobuz => $"qbsearch:{query}",
            SearchType.Tidal => $"tdsearch:{query}",
            SearchType.TidalRecommendations => $"tdrec:{query}",
            SearchType.Letras => $"lmsearch:{query}",
            SearchType.Audius => $"ausearch:{query}",
            SearchType.VkMusic => $"vksearch:{query}",
            SearchType.VkMusicRecommendations => $"vkrec:{query}",
            SearchType.AmazonMusic => $"amazonmusic:{query}",
            SearchType.Bluesky => $"bksearch:{query}",
            SearchType.AppleMusic => $"amsearch:{query}",
            SearchType.NetEase => $"ntsearch:{query}",
            SearchType.YandexMusic => $"ymsearch:{query}",
            SearchType.YandexMusicRecommendations => $"ymrec:{query}",
            SearchType.Bilibili => $"bilisearch:{query}",
            SearchType.LastFm => $"lfsearch:{query}",
            SearchType.GoogleDrive => $"gdsearch:{query}",
            SearchType.Shazam => $"shsearch:{query}",
            SearchType.Deezer => $"dzsearch:{query}",
            SearchType.DeezerRecommendations => $"dzrec:{query}",
            SearchType.Anghami => $"agsearch:{query}",
            SearchType.Audiomack => $"admsearch:{query}",
            SearchType.Pandora => $"pdsearch:{query}",
            SearchType.NicoVideo => $"ncsearch:{query}",
            SearchType.SongLink => $"slsearch:{query}",
            SearchType.Flowery => $"ftts:{query}",
            SearchType.GoogleTts => $"gtts:{query}",
            SearchType.PiperTts => $"pipertts:{query}",
            SearchType.LazyPyTts => $"lazypytts:{query}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(searchType),
                searchType,
                "Unknown search type."
            ),
        };

        RequireNodeLinkSearchSource(searchType);

        return await _node
            .Rest.Tracks.LoadAsync(identifier, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string TrimFormatCharacters(string query)
    {
        var start = 0;
        var end = query.Length;
        while (start < end && char.GetUnicodeCategory(query[start]) == UnicodeCategory.Format)
            start++;
        while (end > start && char.GetUnicodeCategory(query[end - 1]) == UnicodeCategory.Format)
            end--;

        return start == 0 && end == query.Length ? query : query[start..end];
    }

    private void RequireNodeLinkSearchSource(SearchType searchType)
    {
        if (
            searchType
            is SearchType.Spotify
                or SearchType.SpotifyRecommendations
                or SearchType.AppleMusic
                or SearchType.Deezer
                or SearchType.DeezerRecommendations
                or SearchType.YandexMusic
                or SearchType.YandexMusicRecommendations
                or SearchType.VkMusic
                or SearchType.VkMusicRecommendations
                or SearchType.Tidal
                or SearchType.TidalRecommendations
                or SearchType.Qobuz
                or SearchType.QobuzRecommendations
                or SearchType.JioSaavn
                or SearchType.JioSaavnRecommendations
        )
        {
            NodeGuards.RequireNodeLinkOrPlugin(
                _node,
                NodeGuards.LavaSrcPlugin,
                "this search source"
            );
            return;
        }

        if (
            searchType
            is SearchType.Bandcamp
                or SearchType.Monochrome
                or SearchType.YouTubeRecommendations
                or SearchType.LetrasRecommendations
                or SearchType.EternalBox
                or SearchType.Gaana
                or SearchType.IHeartRadio
                or SearchType.Letras
                or SearchType.Audius
                or SearchType.AmazonMusic
                or SearchType.Bluesky
                or SearchType.NetEase
                or SearchType.Bilibili
                or SearchType.LastFm
                or SearchType.GoogleDrive
                or SearchType.Shazam
                or SearchType.Anghami
                or SearchType.Audiomack
                or SearchType.Pandora
                or SearchType.NicoVideo
                or SearchType.SongLink
                or SearchType.Flowery
                or SearchType.GoogleTts
                or SearchType.PiperTts
                or SearchType.LazyPyTts
        )
            NodeGuards.RequireNodeLink(_node, "this search source");
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
            case LoadResult.TrackLoaded loaded:
                loaded.Track.StartPosition = offset;
                break;

            case LoadResult.SearchLoaded search:
                foreach (var track in search.Tracks)
                    track.StartPosition = offset;
                break;

            case LoadResult.PlaylistLoaded playlist:
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
