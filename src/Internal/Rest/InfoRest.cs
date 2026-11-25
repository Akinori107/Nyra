using System.Net;
using System.Text.Json;
using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra.Internal;

internal sealed class InfoRest
{
    private readonly RestClient _rest;

    internal InfoRest(RestClient rest) => _rest = rest;

    public async Task<ServerInfo> GetServerInfoAsync(CancellationToken cancellationToken)
    {
        var json = await _rest
            .SendAsync(HttpMethod.Get, Routes.Info, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return ParseServerInfo(json);
    }

    public async Task<NodeInfo> GetNodeInfoAsync(CancellationToken cancellationToken)
    {
        return await _rest
            .GetExpectingAsync<NodeInfo>(Routes.Info, "info response", cancellationToken)
            .ConfigureAwait(false);
    }

    private static ServerInfo ParseServerInfo(byte[] json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            var isNodeLink = false;
            string? semver = null;
            var plugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var filters = new HashSet<string>(StringComparer.Ordinal);
            var filtersMemberSeen = false;

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("The info response's root is not a single JSON object.");

            while (ReadMember(ref reader, out var member))
            {
                if (member.ValueTextEquals("isNodelink"u8))
                {
                    if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
                        isNodeLink = reader.TokenType is JsonTokenType.True;
                    else
                        reader.Skip();
                }
                else if (member.ValueTextEquals("version"u8))
                {
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        reader.Skip();
                        continue;
                    }

                    while (ReadMember(ref reader, out var versionMember))
                        if (
                            versionMember.ValueTextEquals("semver"u8)
                            && reader.TokenType == JsonTokenType.String
                        )
                            semver ??= reader.GetString();
                        else
                            reader.Skip();
                }
                else if (member.ValueTextEquals("filters"u8))
                {
                    filtersMemberSeen = true;
                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        reader.Skip();
                        continue;
                    }

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        if (
                            reader.TokenType == JsonTokenType.String
                            && reader.GetString() is { } filter
                        )
                            filters.Add(filter);
                        else
                            reader.Skip();
                }
                else if (member.ValueTextEquals("plugins"u8))
                {
                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        reader.Skip();
                        continue;
                    }

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.StartObject)
                        {
                            reader.Skip();
                            continue;
                        }

                        while (ReadMember(ref reader, out var pluginMember))
                            if (
                                pluginMember.ValueTextEquals("name"u8)
                                && reader.TokenType == JsonTokenType.String
                                && reader.GetString() is { } name
                            )
                                plugins.Add(name);
                            else
                                reader.Skip();
                    }
                }
                else
                    reader.Skip();
            }

            if (reader.Read())
                throw new JsonException("The info response carries more than one JSON document.");

            NodeVersion? version = NodeVersion.TryParse(semver, out var parsed) ? parsed : null;

            IReadOnlySet<string>? enabledFilters =
                !isNodeLink && filtersMemberSeen ? filters : null;
            return new ServerInfo(
                isNodeLink,
                semver ?? string.Empty,
                version,
                plugins,
                enabledFilters
            );
        }
        catch (JsonException exception)
        {
            throw new IncompatibleServerException(
                $"The {Routes.Info} response of the server is not valid JSON, only Lavalink v4.2+ and NodeLink v3.9+ are supported.",
                exception
            );
        }
    }

    private static bool ReadMember(ref Utf8JsonReader reader, out Utf8JsonReader member)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            member = reader;
            reader.Read();
            return true;
        }

        member = default;
        return false;
    }

    public async Task<Stats> GetStatsAsync(CancellationToken cancellationToken)
    {
        return await _rest
            .GetExpectingAsync<Stats>(Routes.Stats, "stats report", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ConnectionStatus> GetConnectionStatusAsync(
        CancellationToken cancellationToken
    )
    {
        return await _rest
            .GetExpectingAsync<ConnectionStatus>(
                Routes.Connection,
                "connection status report",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<RoutePlannerStatus> GetRoutePlannerStatusAsync(
        CancellationToken cancellationToken
    )
    {
        var options = _rest.Options;
        var (status, json) = await _rest
            .SendWithStatusAsync(
                HttpMethod.Get,
                Routes.RoutePlannerStatus,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        if (json.Length == 0)
            throw new RestException(
                status,
                Routes.RoutePlannerStatus,
                status == (int)HttpStatusCode.NoContent
                    ? "The server has no route planner configured."
                    : "The server returned an empty route planner status."
            );

        return RestClient.DeserializeOrThrow<RoutePlannerStatus>(
            json,
            Routes.RoutePlannerStatus,
            "route planner status",
            options
        );
    }

    public Task FreeRoutePlannerAddressAsync(string address, CancellationToken cancellationToken)
    {
        var body = NyraJson.SerializeToUtf8Bytes(
            new RoutePlannerFreeAddress(address),
            _rest.Options
        );
        return _rest.SendAsync(
            HttpMethod.Post,
            Routes.RoutePlannerFreeAddress,
            body,
            cancellationToken
        );
    }

    public Task FreeRoutePlannerAddressesAsync(CancellationToken cancellationToken) =>
        _rest.SendAsync(
            HttpMethod.Post,
            Routes.RoutePlannerFreeAll,
            cancellationToken: cancellationToken
        );
}
