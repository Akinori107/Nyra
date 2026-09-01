using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra.Internal;

/// <summary>The kind and version of a server, detected through GET /v4/info.</summary>
internal sealed record ServerInfo(
    bool IsNodeLink,
    string RawVersion,
    NodeVersion? Version,
    IReadOnlySet<string> Plugins
)
{
    /// <summary>Whether Lavalink reported the given plugin as loaded; NodeLink has no plugin list.</summary>
    internal bool HasPlugin(string name) => Plugins.Contains(name);
}

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
    // Lavalink additionally reports "plugins": [{ "name": ... }] for loaded plugins.
    private static ServerInfo ParseServerInfo(byte[] json)
    {
        try
        {
            var reader = new Utf8JsonReader(json);
            var isNodeLink = false;
            string? semver = null;
            var plugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                else if (reader.ValueTextEquals("plugins"u8))
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.StartArray)
                        continue;

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.StartObject)
                            continue;

                        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                        {
                            if (
                                reader.TokenType == JsonTokenType.PropertyName
                                && reader.ValueTextEquals("name"u8)
                            )
                            {
                                reader.Read();
                                if (
                                    reader.TokenType is JsonTokenType.String
                                    && reader.GetString() is { } name
                                )
                                    plugins.Add(name);
                            }
                        }
                    }
                }
            }

            // Dev builds report a commit hash instead of a semver; the version stays null there.
            NodeVersion? version = NodeVersion.TryParse(semver, out var parsed) ? parsed : null;
            return new ServerInfo(isNodeLink, semver ?? "", version, plugins);
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

    // NodeLink wraps the lyrics in a { loadType, data } envelope; LavaLyrics answers with a bare
    // Lyrics object and 204 when nothing was found.
    public async Task<LyricsResult> LoadLyricsAsync(
        string encodedTrack,
        string? language,
        bool skipTrackSource,
        CancellationToken cancellationToken
    )
    {
        if (_node.IsNodeLink)
        {
            var path = string.Concat(
                Endpoints.LoadLyrics,
                "?encodedTrack=",
                Uri.EscapeDataString(encodedTrack)
            );
            if (!string.IsNullOrWhiteSpace(language))
                path = string.Concat(path, "&lang=", Uri.EscapeDataString(language));

            var json = await SendAsync(HttpMethod.Get, path, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return Deserialize<LyricsResult>(json, path)
                ?? throw new RestException(
                    (int)HttpStatusCode.OK,
                    path,
                    "The server returned an empty lyrics result."
                );
        }

        var lavaPath = string.Concat(
            Endpoints.Lyrics,
            "?track=",
            Uri.EscapeDataString(encodedTrack)
        );
        if (skipTrackSource)
            lavaPath = string.Concat(lavaPath, "&skipTrackSource=true");

        var lavaJson = await SendAsync(
                HttpMethod.Get,
                lavaPath,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        if (lavaJson.Length == 0)
            return new LyricsResult { LoadType = LyricsLoadType.Empty };

        return (
            Deserialize<LavaLyricsLyrics>(lavaJson, lavaPath)
            ?? throw new RestException(
                (int)HttpStatusCode.OK,
                lavaPath,
                "The server returned an empty lyrics result."
            )
        ).ToResult();
    }

    public async Task<IReadOnlyList<Chapter>> LoadChaptersAsync(
        string encodedTrack,
        CancellationToken cancellationToken
    )
    {
        var path = string.Concat(
            Endpoints.LoadChapters,
            "?encodedTrack=",
            Uri.EscapeDataString(encodedTrack)
        );
        var json = await SendAsync(HttpMethod.Get, path, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return Deserialize<List<Chapter>>(json, path) ?? [];
    }

    public Task SubscribeLyricsAsync(
        ulong guildId,
        bool skipTrackSource,
        CancellationToken cancellationToken
    ) =>
        SendAsync(
            HttpMethod.Post,
            BuildLyricsSubscriptionPath(guildId, skipTrackSource),
            cancellationToken: cancellationToken
        );

    public Task UnsubscribeLyricsAsync(ulong guildId, CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Delete,
            BuildLyricsSubscriptionPath(guildId, skipTrackSource: false),
            cancellationToken: cancellationToken
        );

    private string BuildLyricsSubscriptionPath(ulong guildId, bool skipTrackSource)
    {
        if (string.IsNullOrEmpty(_node.SessionId))
            throw new InvalidOperationException(
                $"Node '{_node.Name}' has no session id yet; it is not ready."
            );

        var path = string.Format(
            CultureInfo.InvariantCulture,
            Endpoints.LyricsSubscription,
            _node.SessionId,
            guildId
        );
        return skipTrackSource ? path + "?skipTrackSource=true" : path;
    }

    public async Task<SponsorBlockState> GetSponsorBlockAsync(
        ulong guildId,
        CancellationToken cancellationToken
    )
    {
        var path = BuildSponsorBlockPath(guildId);
        var json = await SendAsync(HttpMethod.Get, path, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return Deserialize<SponsorBlockState>(json, path)
            ?? throw new RestException(
                (int)HttpStatusCode.OK,
                path,
                "The server returned an empty SponsorBlock state."
            );
    }

    public async Task<SponsorBlockState> SetSponsorBlockAsync(
        ulong guildId,
        SponsorBlockSettings settings,
        CancellationToken cancellationToken
    )
    {
        var path = BuildSponsorBlockPath(guildId);
        var body = JsonSerializer.SerializeToUtf8Bytes(settings, Options);
        var json = await SendAsync(HttpMethod.Patch, path, body, cancellationToken)
            .ConfigureAwait(false);
        return Deserialize<SponsorBlockState>(json, path)
            ?? throw new RestException(
                (int)HttpStatusCode.OK,
                path,
                "The server returned an empty SponsorBlock state."
            );
    }

    public async Task<SponsorBlockState> SetSponsorBlockSegmentsAsync(
        ulong guildId,
        IReadOnlyList<SponsorBlockSegment> segments,
        CancellationToken cancellationToken
    )
    {
        var path = BuildSponsorBlockPath(guildId);
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new SponsorBlockSegmentOverride(segments),
            Options
        );
        var json = await SendAsync(HttpMethod.Post, path, body, cancellationToken)
            .ConfigureAwait(false);
        return Deserialize<SponsorBlockState>(json, path)
            ?? throw new RestException(
                (int)HttpStatusCode.OK,
                path,
                "The server returned an empty SponsorBlock state."
            );
    }

    public Task ClearSponsorBlockAsync(ulong guildId, CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Delete,
            BuildSponsorBlockPath(guildId),
            cancellationToken: cancellationToken
        );

    private string BuildSponsorBlockPath(ulong guildId)
    {
        if (string.IsNullOrEmpty(_node.SessionId))
            throw new InvalidOperationException(
                $"Node '{_node.Name}' has no session id yet; it is not ready."
            );

        return string.Format(
            CultureInfo.InvariantCulture,
            Endpoints.SponsorBlock,
            _node.SessionId,
            guildId
        );
    }

    public async Task<MixLayer> CreateMixLayerAsync(
        ulong guildId,
        string encodedTrack,
        float? volume,
        CancellationToken cancellationToken
    )
    {
        var path = BuildMixPath(guildId);
        var body = JsonSerializer.SerializeToUtf8Bytes(
            new MixLayerCreate(new PlayerUpdateTrack { Encoded = encodedTrack }, volume),
            Options
        );
        var json = await SendAsync(HttpMethod.Post, path, body, cancellationToken)
            .ConfigureAwait(false);
        return Deserialize<MixLayer>(json, path)
            ?? throw new RestException(
                (int)HttpStatusCode.OK,
                path,
                "The server returned an empty mix layer."
            );
    }

    public async Task<IReadOnlyList<MixLayer>> ListMixLayersAsync(
        ulong guildId,
        CancellationToken cancellationToken
    )
    {
        var path = BuildMixPath(guildId);
        var json = await SendAsync(HttpMethod.Get, path, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return Deserialize<MixLayerCollection>(json, path)?.Mixes ?? [];
    }

    public Task SetMixLayerVolumeAsync(
        ulong guildId,
        string mixLayerId,
        float volume,
        CancellationToken cancellationToken
    )
    {
        var path = BuildMixLayerPath(guildId, mixLayerId);
        var body = JsonSerializer.SerializeToUtf8Bytes(new MixLayerVolume(volume), Options);
        return SendAsync(HttpMethod.Patch, path, body, cancellationToken);
    }

    public Task RemoveMixLayerAsync(
        ulong guildId,
        string mixLayerId,
        CancellationToken cancellationToken
    ) =>
        SendAsync(
            HttpMethod.Delete,
            BuildMixLayerPath(guildId, mixLayerId),
            cancellationToken: cancellationToken
        );

    private string BuildMixPath(ulong guildId)
    {
        if (string.IsNullOrEmpty(_node.SessionId))
            throw new InvalidOperationException(
                $"Node '{_node.Name}' has no session id yet; it is not ready."
            );

        return string.Format(CultureInfo.InvariantCulture, Endpoints.Mix, _node.SessionId, guildId);
    }

    private string BuildMixLayerPath(ulong guildId, string mixLayerId)
    {
        if (string.IsNullOrEmpty(_node.SessionId))
            throw new InvalidOperationException(
                $"Node '{_node.Name}' has no session id yet; it is not ready."
            );

        return string.Format(
            CultureInfo.InvariantCulture,
            Endpoints.MixLayer,
            _node.SessionId,
            guildId,
            mixLayerId
        );
    }

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

// LavaLyrics wire shape: a bare Lyrics object rather than NodeLink's { loadType, data } envelope.
internal sealed record LavaLyricsLyrics(
    [property: JsonPropertyName("sourceName")] string SourceName,
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("lines")] IReadOnlyList<LavaLyricsLine>? Lines
)
{
    public LyricsResult ToResult() =>
        new()
        {
            LoadType = LyricsLoadType.Lyrics,
            Data = new LyricsData
            {
                Name = SourceName,
                Synced = Lines is not null,
                Lines = Lines
                    ?.Select(line => new LyricsLine
                    {
                        Text = line.Line,
                        Time = line.Timestamp,
                        Duration = line.Duration ?? 0,
                    })
                    .ToList(),
                Provider = Provider,
                Text = Text,
            },
        };
}

internal sealed record LavaLyricsLine(
    [property: JsonPropertyName("timestamp")] long Timestamp,
    [property: JsonPropertyName("duration")] long? Duration,
    [property: JsonPropertyName("line")] string Line
);
