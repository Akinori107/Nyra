using Nyra.Entities;

namespace Nyra.Internal;

internal sealed class YouTubeRest
{
    private readonly RestClient _rest;

    internal YouTubeRest(RestClient rest) => _rest = rest;

    public async Task<YouTubeConfig> GetConfigAsync(
        bool validate,
        CancellationToken cancellationToken
    )
    {
        var path = validate ? Routes.YouTubeConfig + "?validate=true" : Routes.YouTubeConfig;
        return await _rest
            .SendExpectingAsync<YouTubeConfig>(
                HttpMethod.Get,
                path,
                "YouTube config",
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<YouTubeConfigUpdate> UpdateConfigAsync(
        YouTubeConfigPatch payload,
        CancellationToken cancellationToken
    )
    {
        var options = _rest.Options;
        var body = NyraJson.SerializeToUtf8Bytes(payload, options);
        return await _rest
            .SendExpectingAsync<YouTubeConfigUpdate>(
                HttpMethod.Patch,
                Routes.YouTubeConfig,
                "update response",
                body,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<YouTubeToken> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken
    )
    {
        var options = _rest.Options;
        var body = NyraJson.SerializeToUtf8Bytes(new YouTubeTokenRefresh(refreshToken), options);
        return await _rest
            .SendExpectingAsync<YouTubeToken>(
                HttpMethod.Post,
                Routes.YouTubeOAuth,
                "token response",
                body,
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
