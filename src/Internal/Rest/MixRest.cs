using Nyra.Entities;

namespace Nyra.Internal;

internal sealed class MixRest
{
    private readonly RestClient _rest;

    internal MixRest(RestClient rest) => _rest = rest;

    public async Task<MixLayer> CreateAsync(
        ulong guildId,
        string encodedTrack,
        float? volume,
        CancellationToken cancellationToken
    )
    {
        var path = BuildMixPath(guildId);
        var options = _rest.Options;
        var body = NyraJson.SerializeToUtf8Bytes(
            new MixLayerCreate(new PlayerUpdateTrack { Encoded = encodedTrack }, volume),
            options
        );
        return await _rest
            .SendExpectingAsync<MixLayer>(
                HttpMethod.Post,
                path,
                "mix layer",
                body,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MixLayer>> ListAsync(
        ulong guildId,
        CancellationToken cancellationToken
    )
    {
        var path = BuildMixPath(guildId);
        var options = _rest.Options;
        var json = await _rest
            .SendAsync(HttpMethod.Get, path, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return RestClient.RequireNoNullElements(
            _rest.Deserialize<MixLayerCollection>(json, path, options)?.Mixes ?? [],
            path
        );
    }

    public Task SetVolumeAsync(
        ulong guildId,
        string mixLayerId,
        float volume,
        CancellationToken cancellationToken
    )
    {
        var path = BuildMixLayerPath(guildId, mixLayerId);
        var body = NyraJson.SerializeToUtf8Bytes(new MixLayerVolume(volume), _rest.Options);
        return _rest.SendAsync(HttpMethod.Patch, path, body, cancellationToken);
    }

    public Task RemoveAsync(
        ulong guildId,
        string mixLayerId,
        CancellationToken cancellationToken
    ) =>
        _rest.SendAsync(
            HttpMethod.Delete,
            BuildMixLayerPath(guildId, mixLayerId),
            cancellationToken: cancellationToken
        );

    private string BuildMixPath(ulong guildId) => _rest.SessionPath(Routes.Mix, guildId);

    private string BuildMixLayerPath(ulong guildId, string mixLayerId) =>
        _rest.SessionPath(Routes.MixLayer, guildId, Uri.EscapeDataString(mixLayerId));
}
