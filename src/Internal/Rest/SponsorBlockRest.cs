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
        return await _rest
            .SendJsonAsync<SponsorBlockState, SponsorBlockSettings>(
                HttpMethod.Patch,
                path,
                "SponsorBlock state",
                settings,
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
        return await _rest
            .SendJsonAsync<SponsorBlockState, SponsorBlockSegmentOverride>(
                HttpMethod.Post,
                path,
                "SponsorBlock state",
                new SponsorBlockSegmentOverride(segments),
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
