using Nyra.Entities;

namespace Nyra.Internal;

internal sealed class SponsorBlockRest
{
    private readonly RestClient _rest;

    internal SponsorBlockRest(RestClient rest) => _rest = rest;

    public async Task<SponsorBlockState> GetAsync(
        ulong guildId,
        CancellationToken cancellationToken
    )
    {
        var path = BuildSponsorBlockPath(guildId);
        return await _rest
            .SendExpectingAsync<SponsorBlockState>(
                HttpMethod.Get,
                path,
                "SponsorBlock state",
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<SponsorBlockState> SetAsync(
        ulong guildId,
        SponsorBlockSettings settings,
        CancellationToken cancellationToken
    )
    {
        var path = BuildSponsorBlockPath(guildId);
        var options = _rest.Options;
        var body = NyraJson.SerializeToUtf8Bytes(settings, options);
        return await _rest
            .SendExpectingAsync<SponsorBlockState>(
                HttpMethod.Patch,
                path,
                "SponsorBlock state",
                body,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<SponsorBlockState> SetSegmentsAsync(
        ulong guildId,
        IReadOnlyList<SponsorBlockSegment> segments,
        CancellationToken cancellationToken
    )
    {
        var path = BuildSponsorBlockPath(guildId);
        var options = _rest.Options;
        var body = NyraJson.SerializeToUtf8Bytes(
            new SponsorBlockSegmentOverride(segments),
            options
        );
        return await _rest
            .SendExpectingAsync<SponsorBlockState>(
                HttpMethod.Post,
                path,
                "SponsorBlock state",
                body,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public Task ClearAsync(ulong guildId, CancellationToken cancellationToken) =>
        _rest.SendAsync(
            HttpMethod.Delete,
            BuildSponsorBlockPath(guildId),
            cancellationToken: cancellationToken
        );

    private string BuildSponsorBlockPath(ulong guildId) =>
        _rest.SessionPath(Routes.SponsorBlock, guildId);
}
