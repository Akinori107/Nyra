using Nyra.Entities;

namespace Nyra.Internal;

internal sealed class LyricsRest
{
    private readonly RestClient _rest;

    internal LyricsRest(RestClient rest) => _rest = rest;

    public async Task<LyricsResult> LoadAsync(
        string encodedTrack,
        string? language,
        bool skipTrackSource,
        string? source,
        CancellationToken cancellationToken
    )
    {
        var options = _rest.Options;
        if (_rest.Node.IsNodeLink)
        {
            var path = string.Concat(
                Routes.LoadLyrics,
                "?encodedTrack=",
                Uri.EscapeDataString(encodedTrack)
            );
            if (!string.IsNullOrWhiteSpace(language))
                path = string.Concat(path, "&lang=", Uri.EscapeDataString(language));
            if (!string.IsNullOrWhiteSpace(source))
                path = string.Concat(path, "&source=", Uri.EscapeDataString(source));

            return await _rest
                .SendExpectingAsync<LyricsResult>(
                    HttpMethod.Get,
                    path,
                    "lyrics result",
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }

        var lavaPath = string.Concat(Routes.Lyrics, "?track=", Uri.EscapeDataString(encodedTrack));
        if (skipTrackSource)
            lavaPath = string.Concat(lavaPath, "&skipTrackSource=true");

        var lavaJson = await _rest
            .SendAsync(HttpMethod.Get, lavaPath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (lavaJson.Length == 0)
            return new LyricsResult { LoadType = LyricsLoadType.Empty };

        return _rest
            .DeserializeOrThrow<LavaLyricsLyrics>(lavaJson, lavaPath, "lyrics result", options)
            .ToResult();
    }

    public async Task<IReadOnlyList<Chapter>> LoadChaptersAsync(
        string encodedTrack,
        CancellationToken cancellationToken
    )
    {
        var path = string.Concat(
            Routes.LoadChapters,
            "?encodedTrack=",
            Uri.EscapeDataString(encodedTrack)
        );
        var options = _rest.Options;
        var json = await _rest
            .SendAsync(HttpMethod.Get, path, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return _rest.DeserializeList<Chapter>(json, path, options);
    }

    public async Task<MeaningResult> LoadMeaningAsync(
        string encodedTrack,
        string? language,
        CancellationToken cancellationToken
    )
    {
        var path = string.Concat(
            Routes.Meaning,
            "?encodedTrack=",
            Uri.EscapeDataString(encodedTrack)
        );
        if (!string.IsNullOrWhiteSpace(language))
            path = string.Concat(path, "&lang=", Uri.EscapeDataString(language));

        return await _rest
            .SendExpectingAsync<MeaningResult>(
                HttpMethod.Get,
                path,
                "meaning result",
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    public Task SubscribeAsync(
        ulong guildId,
        bool skipTrackSource,
        CancellationToken cancellationToken
    ) =>
        _rest.SendAsync(
            HttpMethod.Post,
            BuildLyricsSubscriptionPath(guildId, skipTrackSource),
            cancellationToken: cancellationToken
        );

    public Task UnsubscribeAsync(ulong guildId, CancellationToken cancellationToken) =>
        _rest.SendAsync(
            HttpMethod.Delete,
            BuildLyricsSubscriptionPath(guildId, skipTrackSource: false),
            cancellationToken: cancellationToken
        );

    private string BuildLyricsSubscriptionPath(ulong guildId, bool skipTrackSource)
    {
        var path = _rest.SessionPath(Routes.LyricsSubscription, guildId);
        return skipTrackSource ? path + "?skipTrackSource=true" : path;
    }
}
