using Nyra.Entities;

namespace Nyra.Internal;

internal sealed class GroupsRest
{
    private readonly RestClient _rest;

    internal GroupsRest(RestClient rest) => _rest = rest;

    public async Task<IReadOnlyList<Group>> ListAsync(CancellationToken cancellationToken) =>
        await _rest.GetListAsync<Group>(BuildGroupsPath(), cancellationToken).ConfigureAwait(false);

    public async Task<Group> CreateAsync(
        string groupId,
        IReadOnlyList<ulong>? guildIds,
        CancellationToken cancellationToken
    )
    {
        var path = BuildGroupsPath();
        var options = _rest.Options;
        var body = NyraJson.SerializeToUtf8Bytes(
            new GroupCreatePayload(groupId, guildIds),
            options
        );
        return await _rest
            .SendExpectingAsync<Group>(
                HttpMethod.Post,
                path,
                "group response",
                body,
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
        var options = _rest.Options;
        var body = NyraJson.SerializeToUtf8Bytes(update, options);
        return await _rest
            .SendExpectingAsync<GroupUpdateResult>(
                HttpMethod.Patch,
                path,
                "group response",
                body,
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
