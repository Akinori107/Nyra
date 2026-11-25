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
        return await _rest
            .SendJsonAsync<YouTubeConfigUpdate, YouTubeConfigPatch>(
                HttpMethod.Patch,
                Routes.YouTubeConfig,
                "update response",
                payload,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<YouTubeToken> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken
    )
    {
        return await _rest
            .SendJsonAsync<YouTubeToken, YouTubeTokenRefresh>(
                HttpMethod.Post,
                Routes.YouTubeOAuth,
                "token response",
                new YouTubeTokenRefresh(refreshToken),
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
