using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra.Internal;

/// <summary>The kind and version of a server, detected through GET /v4/info.</summary>
internal sealed record ServerInfo(bool IsNodeLink, string RawVersion, NodeVersion? Version);

/// <summary>REST client of a node; the process-wide <see cref="HttpClient"/> is shared by all nodes.</summary>
internal sealed class RestClient
{
    private static readonly HttpClient Http = new(
        new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            // Fail fast on dead nodes instead of hanging on the connect phase.
            ConnectTimeout = TimeSpan.FromSeconds(10),
        }
    )
    {
        // Bounds the buffered response of a hostile node; the default would allow gigabytes.
        MaxResponseContentBufferSize = 64 * 1024 * 1024,
    };

    private readonly Node _node;
    private readonly Uri _baseUri;

    public RestClient(Node node)
    {
        _node = node;
        _baseUri = node.Endpoint.ToRestUri();
    }

    // Response shapes differ between the servers; the detected kind picks the converters.
    private JsonSerializerOptions Options => NyraJson.OptionsFor(_node.IsNodeLink);

    public async Task<ServerInfo> GetServerInfoAsync(CancellationToken cancellationToken)
    {
        var json = await SendAsync(
                HttpMethod.Get,
                Endpoints.Info,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return ParseServerInfo(json);
    }

    // NodeLink sets "isNodelink": true; both servers report "version" -> "semver".
    private static ServerInfo ParseServerInfo(byte[] json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            var isNodeLink = false;
            string? semver = null;

            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                    continue;

                if (reader.ValueTextEquals("isNodelink"u8))
                {
                    reader.Read();
                    isNodeLink = reader.TokenType is JsonTokenType.True;
                }
                else if (reader.ValueTextEquals("semver"u8))
                {
                    reader.Read();
                    if (reader.TokenType is JsonTokenType.String)
                        semver = reader.GetString();
                }
            }

            // Dev builds report a commit hash instead of a semver; the version stays null there.
            NodeVersion? version = NodeVersion.TryParse(semver, out var parsed) ? parsed : null;
            return new ServerInfo(isNodeLink, semver ?? "", version);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The server at {Endpoints.Info} returned a body that is not valid JSON; only Lavalink v4 and NodeLink v3+ are supported.",
                exception
            );
        }
    }

    /// <summary>Gets the authoritative player state; null when the server has no player for the guild.</summary>
    public async Task<Player?> GetPlayerAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var path = BuildPlayerPath(guildId);
        try
        {
            var json = await SendAsync(HttpMethod.Get, path, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return Deserialize<Player>(json, path);
        }
        catch (RestException exception) when (exception.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<LoadResult> LoadTracksAsync(
        string identifier,
        CancellationToken cancellationToken
    )
    {
        var path = string.Concat(
            Endpoints.LoadTracks,
            "?identifier=",
            Uri.EscapeDataString(identifier)
        );
        var json = await SendAsync(HttpMethod.Get, path, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return Deserialize<LoadResult>(json, path)
            ?? throw new RestException(
                (int)HttpStatusCode.OK,
                path,
                "The server returned an empty load result."
            );
    }

    public async Task<Player> UpdatePlayerAsync(
        ulong guildId,
        PlayerUpdatePayload payload,
        bool noReplace,
        CancellationToken cancellationToken
    )
    {
        var path = BuildPlayerPath(guildId);
        if (noReplace)
            path += "?noReplace=true";

        var body = JsonSerializer.SerializeToUtf8Bytes(payload, Options);
        var json = await SendAsync(HttpMethod.Patch, path, body, cancellationToken)
            .ConfigureAwait(false);
        return Deserialize<Player>(json, path)
            ?? throw new RestException(
                (int)HttpStatusCode.OK,
                path,
                "The server returned an empty player response."
            );
    }

    public Task DestroyPlayerAsync(ulong guildId, CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Delete,
            BuildPlayerPath(guildId),
            cancellationToken: cancellationToken
        );

    public async Task<Session> UpdateSessionAsync(
        bool resuming,
        int timeoutSeconds,
        CancellationToken cancellationToken
    )
    {
        var path = BuildSessionPath();
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new Session { Resuming = resuming, Timeout = timeoutSeconds },
            Options
        );
        var json = await SendAsync(HttpMethod.Patch, path, body, cancellationToken)
            .ConfigureAwait(false);
        return Deserialize<Session>(json, path)
            ?? throw new RestException(
                (int)HttpStatusCode.OK,
                path,
                "The server returned an empty session response."
            );
    }

    private string BuildPlayerPath(ulong guildId)
    {
        if (string.IsNullOrEmpty(_node.SessionId))
            throw new InvalidOperationException(
                $"Node '{_node.Name}' has no session id yet; it is not ready."
            );

        return string.Format(
            CultureInfo.InvariantCulture,
            Endpoints.Player,
            _node.SessionId,
            guildId
        );
    }

    private string BuildSessionPath()
    {
        if (string.IsNullOrEmpty(_node.SessionId))
            throw new InvalidOperationException(
                $"Node '{_node.Name}' has no session id yet; it is not ready."
            );

        return string.Format(CultureInfo.InvariantCulture, Endpoints.Session, _node.SessionId);
    }

    private T? Deserialize<T>(byte[] json, string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new RestException(
                (int)HttpStatusCode.OK,
                path,
                $"The server returned malformed JSON: {exception.Message}"
            );
        }
    }

    /// <summary>Sends the request and returns the raw response body; only successful responses get here.</summary>
    private async Task<byte[]> SendAsync(
        HttpMethod method,
        string path,
        byte[]? jsonBody = null,
        CancellationToken cancellationToken = default
    )
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
        request.Headers.TryAddWithoutValidation("Authorization", _node.Endpoint.Password);
        request.Headers.TryAddWithoutValidation("User-Agent", ClientInfo.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        if (jsonBody is not null)
            request.Content = new ByteArrayContent(jsonBody)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
            };

        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response
                .Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            throw new RestException((int)response.StatusCode, path, body);
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }
}
