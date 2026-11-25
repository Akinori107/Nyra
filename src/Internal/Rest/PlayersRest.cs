using Nyra.Entities;

namespace Nyra.Internal;

internal sealed class PlayersRest(RestClient rest)
{
    private readonly RestClient _rest = rest;

    public async Task<Player?> GetAsync(ulong guildId, CancellationToken cancellationToken) =>
        await _rest
            .GetOptionalAsync<Player>(BuildPlayerPath(guildId), cancellationToken)
            .ConfigureAwait(false);

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

    public async Task<IReadOnlyList<Player>> ListAsync(CancellationToken cancellationToken) =>
        await _rest
            .GetListAsync<Player>(BuildPlayersPath(), cancellationToken)
            .ConfigureAwait(false);

    private string BuildPlayersPath() => _rest.SessionPath(Routes.Players);

    private string BuildPlayerPath(ulong guildId) => _rest.SessionPath(Routes.Player, guildId);
}
