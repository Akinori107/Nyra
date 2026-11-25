using Nyra.Entities;

namespace Nyra.Internal;

internal sealed class GroupsRest(RestClient rest)
{
    private readonly RestClient _rest = rest;

    public async Task<IReadOnlyList<Group>> ListAsync(CancellationToken cancellationToken) =>
        await _rest.GetListAsync<Group>(BuildGroupsPath(), cancellationToken).ConfigureAwait(false);

    public async Task<Group> CreateAsync(
        string groupId,
        IReadOnlyList<ulong>? guildIds,
        CancellationToken cancellationToken
    )
    {
        var path = BuildGroupsPath();
        return await _rest
            .SendJsonAsync<Group, GroupCreatePayload>(
                HttpMethod.Post,
                path,
                "group response",
                new GroupCreatePayload(groupId, guildIds),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<Group?> GetAsync(string groupId, CancellationToken cancellationToken) =>
        await _rest
            .GetOptionalAsync<Group>(BuildGroupPath(groupId), cancellationToken)
            .ConfigureAwait(false);

    public async Task<GroupUpdateResult> UpdateAsync(
        string groupId,
        GroupUpdate update,
        CancellationToken cancellationToken
    )
    {
        var path = BuildGroupPath(groupId);
        return await _rest
            .SendJsonAsync<GroupUpdateResult, GroupUpdate>(
                HttpMethod.Patch,
                path,
                "group response",
                update,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public Task DeleteAsync(string groupId, CancellationToken cancellationToken) =>
        _rest.SendAsync(
            HttpMethod.Delete,
            BuildGroupPath(groupId),
            cancellationToken: cancellationToken
        );

    private string BuildGroupsPath() => _rest.SessionPath(Routes.Groups);

    private string BuildGroupPath(string groupId) =>
        _rest.SessionPath(Routes.Group, Uri.EscapeDataString(groupId));
}
