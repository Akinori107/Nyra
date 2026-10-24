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
            .SendExpectingAsync<NodeInfo>(
                HttpMethod.Get,
                Routes.Info,
                "info response",
                cancellationToken: cancellationToken
            )
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

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    break;

                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals("isNodelink"u8))
                {
                    reader.Read();
                    if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
                        isNodeLink = reader.TokenType is JsonTokenType.True;
                    else
                        reader.Skip();
                }
                else if (reader.ValueTextEquals("version"u8))
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        reader.Skip();
                        continue;
                    }

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName)
                            continue;

                        if (reader.ValueTextEquals("semver"u8))
                        {
                            reader.Read();
                            if (reader.TokenType is JsonTokenType.String)
                                semver ??= reader.GetString();
                            else
                                reader.Skip();
                        }
                        else
                        {
                            reader.Read();
                            reader.Skip();
                        }
                    }
                }
                else if (reader.ValueTextEquals("filters"u8))
                {
                    filtersMemberSeen = true;
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        reader.Skip();
                        continue;
                    }

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        if (
                            reader.TokenType is JsonTokenType.String
                            && reader.GetString() is { } filter
                        )
                            filters.Add(filter);
                        else
                            reader.Skip();
                }
                else if (reader.ValueTextEquals("plugins"u8))
                {
                    reader.Read();
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

                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            var member = reader;
                            reader.Read();

                            if (
                                member.ValueTextEquals("name"u8)
                                && reader.TokenType is JsonTokenType.String
                                && reader.GetString() is { } name
                            )
                                plugins.Add(name);
                            else
                                reader.Skip();
                        }
                    }
                }
                else
                {
                    reader.Read();
                    reader.Skip();
                }
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

    public async Task<Stats> GetStatsAsync(CancellationToken cancellationToken)
    {
        return await _rest
            .SendExpectingAsync<Stats>(
                HttpMethod.Get,
                Routes.Stats,
                "stats report",
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<ConnectionStatus> GetConnectionStatusAsync(
        CancellationToken cancellationToken
    )
    {
        return await _rest
            .SendExpectingAsync<ConnectionStatus>(
                HttpMethod.Get,
                Routes.Connection,
                "connection status report",
                cancellationToken: cancellationToken
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

        return _rest.DeserializeOrThrow<RoutePlannerStatus>(
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
