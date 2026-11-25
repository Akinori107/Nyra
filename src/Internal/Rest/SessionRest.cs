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
        return await _rest
            .SendJsonAsync<SessionConfig, SessionConfig>(
                HttpMethod.Patch,
                path,
                "session response",
                new SessionConfig { Resuming = resuming, Timeout = timeoutSeconds },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private string BuildSessionPath() => _rest.SessionPath(Routes.Session);
}
