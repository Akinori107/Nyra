using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra.Internal;

internal sealed class PlayersRest
{
    private readonly RestClient _rest;

    internal PlayersRest(RestClient rest) => _rest = rest;

    public async Task<Player?> GetAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var path = BuildPlayerPath(guildId);
        var options = _rest.Options;
        try
        {
            var json = await _rest
                .SendAsync(HttpMethod.Get, path, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return RestClient.Deserialize<Player>(json, path, options);
        }
        catch (RestException exception) when (RestClient.IndicatesMissingResource(exception))
        {
            return null;
        }
    }

    public async Task<Player> UpdateAsync<TPayload>(
        ulong guildId,
        TPayload payload,
        CancellationToken cancellationToken,
        bool noReplace = false,
        TimeSpan? timeout = null
    )
    {
        var body = NyraJson.SerializeToUtf8Bytes(payload, _rest.Options);
        return await _rest
            .SendExpectingAsync<Player>(
                HttpMethod.Patch,
                BuildPlayerPath(guildId) + (noReplace ? "?noReplace=true" : string.Empty),
                "player response",
                body,
                timeout ?? RestClient.RequestTimeout,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public Task DestroyAsync(ulong guildId, CancellationToken cancellationToken) =>
        _rest.SendAsync(
            HttpMethod.Delete,
            BuildPlayerPath(guildId),
            cancellationToken: cancellationToken
        );

    public async Task<IReadOnlyList<Player>> ListAsync(CancellationToken cancellationToken)
    {
        var path = BuildPlayersPath();
        var options = _rest.Options;
        var json = await _rest
            .SendAsync(HttpMethod.Get, path, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return RestClient.DeserializeList<Player>(json, path, options);
    }

    private string BuildPlayersPath() => _rest.SessionPath(Routes.Players);

    private string BuildPlayerPath(ulong guildId) => _rest.SessionPath(Routes.Player, guildId);
}
