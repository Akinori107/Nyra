using Nyra.Entities;

namespace Nyra.Internal;

internal sealed class SessionRest
{
    private readonly RestClient _rest;

    internal SessionRest(RestClient rest) => _rest = rest;

    public async Task<SessionConfig> UpdateAsync(
        bool resuming,
        int timeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        var path = BuildSessionPath();
        var options = _rest.Options;
        var body = NyraJson.SerializeToUtf8Bytes(
            new SessionConfig { Resuming = resuming, Timeout = timeoutSeconds },
            options
        );
        return await _rest
            .SendExpectingAsync<SessionConfig>(
                HttpMethod.Patch,
                path,
                "session response",
                body,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private string BuildSessionPath() => _rest.SessionPath(Routes.Session);
}
