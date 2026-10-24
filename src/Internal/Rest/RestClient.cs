using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Nyra.Exceptions;

namespace Nyra.Internal;

internal sealed class RestClient
{
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(10),
    };

    private static readonly HttpClient Http = new(SharedHandler, disposeHandler: false)
    {
        MaxResponseContentBufferSize = 64 * 1024 * 1024,
        Timeout = TimeSpan.FromSeconds(60),
    };

    internal static readonly HttpClient StreamingHttp = new(SharedHandler, disposeHandler: false)
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly Node _node;

    private readonly Uri _baseUri;

    private static readonly MediaTypeHeaderValue JsonContentType = new("application/json");

    public RestClient(Node node)
    {
        _node = node;
        _baseUri = node.Endpoint.ToRestUri();
        Info = new InfoRest(this);
        Players = new PlayersRest(this);
        Tracks = new TracksRest(this);
        Groups = new GroupsRest(this);
        Lyrics = new LyricsRest(this);
        SponsorBlock = new SponsorBlockRest(this);
        Mix = new MixRest(this);
        Session = new SessionRest(this);
        YouTube = new YouTubeRest(this);
    }

    internal Node Node => _node;

    internal Uri BaseUri => _baseUri;

    internal InfoRest Info { get; }

    internal PlayersRest Players { get; }

    internal TracksRest Tracks { get; }

    internal GroupsRest Groups { get; }

    internal LyricsRest Lyrics { get; }

    internal SponsorBlockRest SponsorBlock { get; }

    internal MixRest Mix { get; }

    internal SessionRest Session { get; }

    internal YouTubeRest YouTube { get; }

    private volatile FilteredOptions? _filteredOptions;

    private sealed record FilteredOptions(
        JsonSerializerOptions Value,
        IReadOnlySet<string> Filters
    );

    internal JsonSerializerOptions Options
    {
        get
        {
            var info = _node.ServerInfo;
            var isNodeLink = info?.IsNodeLink ?? false;
            if (isNodeLink || info?.EnabledFilters is not { } filters)
                return NyraJson.OptionsFor(isNodeLink);

            var cache = _filteredOptions;
            if (cache is null || !SameFilters(cache.Filters, filters))
            {
                cache = new FilteredOptions(NyraJson.OptionsFor(filters), filters);
                _filteredOptions = cache;
            }

            return cache.Value;
        }
    }

    private static bool SameFilters(IReadOnlySet<string> cached, IReadOnlySet<string> current) =>
        ReferenceEquals(cached, current)
        || (cached.Count == current.Count && cached.SetEquals(current));

    internal static bool IndicatesMissingSession(RestException exception)
    {
        var body = exception.ResponseBody;
        if (body is null)
            return false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                return doc.RootElement.TryGetProperty("message", out var message)
                    && message.ValueKind == JsonValueKind.String
                    && message.GetString() is { } text
                    && text.Contains("session", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("No endpoint", StringComparison.Ordinal);
        }
        catch (JsonException) { }

        return body.Contains("session", StringComparison.OrdinalIgnoreCase);
    }

    internal T? Deserialize<T>(byte[] json, string path, JsonSerializerOptions options)
    {
        try
        {
            return NyraJson.Deserialize<T>(json, options);
        }
        catch (JsonException exception)
        {
            throw new RestException(
                (int)HttpStatusCode.OK,
                path,
                $"The server returned malformed JSON: {exception.Message}",
                retryAfter: null,
                innerException: exception
            );
        }
    }

    internal T DeserializeOrThrow<T>(
        byte[] json,
        string path,
        string what,
        JsonSerializerOptions options
    ) =>
        Deserialize<T>(json, path, options)
        ?? throw new RestException(
            (int)HttpStatusCode.OK,
            path,
            $"The server returned an empty {what}."
        );

    internal IReadOnlyList<T> DeserializeList<T>(
        byte[] json,
        string path,
        JsonSerializerOptions options
    ) => RequireNoNullElements(Deserialize<List<T>>(json, path, options) ?? [], path);

    internal static IReadOnlyList<T> RequireNoNullElements<T>(IReadOnlyList<T> list, string path)
    {
        foreach (var item in list)
            if (item is null)
                throw new RestException(
                    (int)HttpStatusCode.OK,
                    path,
                    "The server returned a null element in the response list."
                );
        return list;
    }

    private string RequireSessionId() =>
        _node.SessionId is { Length: > 0 } sessionId
            ? sessionId
            : throw new InvalidOperationException(
                $"Node '{_node.Name}' has no usable session id: the node is not ready yet, or the ready's id was dropped as header-hostile."
            );

    internal string SessionPath(string route, params object[] arguments) =>
        string.Format(
            CultureInfo.InvariantCulture,
            route,
            [Uri.EscapeDataString(RequireSessionId()), .. arguments]
        );

    internal async Task<T> SendExpectingAsync<T>(
        HttpMethod method,
        string path,
        string what,
        byte[]? jsonBody = null,
        CancellationToken cancellationToken = default
    )
    {
        var options = Options;
        var json = await SendAsync(method, path, jsonBody, cancellationToken).ConfigureAwait(false);
        return DeserializeOrThrow<T>(json, path, what, options);
    }

    internal async Task<byte[]> SendAsync(
        HttpMethod method,
        string path,
        byte[]? jsonBody = null,
        CancellationToken cancellationToken = default
    ) =>
        (
            await SendWithStatusAsync(method, path, jsonBody, cancellationToken)
                .ConfigureAwait(false)
        ).Body;

    internal async Task<(int Status, byte[] Body)> SendWithStatusAsync(
        HttpMethod method,
        string path,
        byte[]? jsonBody = null,
        CancellationToken cancellationToken = default
    )
    {
        using var request = CreateRequest(method, path, "application/json");

        if (jsonBody is not null)
            request.Content = new ByteArrayContent(jsonBody)
            {
                Headers = { ContentType = JsonContentType },
            };

        try
        {
            using var response = await Http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                )
                .ConfigureAwait(false);

            using var bodyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bodyCts.CancelAfter(Http.Timeout);

            if (!response.IsSuccessStatusCode)
            {
                var retryAfter = ReadRetryAfter(response);
                string body;
                try
                {
                    body = await response
                        .Content.ReadAsStringAsync(bodyCts.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception readFailure) when (readFailure is not OperationCanceledException)
                {
                    body = string.Empty;
                }
                throw new RestException((int)response.StatusCode, path, body, retryAfter);
            }

            var statusCode = (int)response.StatusCode;
            return (
                statusCode,
                await response.Content.ReadAsByteArrayAsync(bodyCts.Token).ConfigureAwait(false)
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The node '{_node.Name}' at {_node.Endpoint} did not answer the request in time, the client's own bound fired and the caller's token is intact."
            );
        }
        catch (HttpRequestException exception)
        {
            throw new NodeUnreachableException(
                $"The node '{_node.Name}' at {_node.Endpoint} is not reachable: {exception.Message}",
                exception
            );
        }
        catch (IOException exception)
        {
            throw new NodeUnreachableException(
                $"The node '{_node.Name}' at {_node.Endpoint} is not reachable: {exception.Message}",
                exception
            );
        }
    }

    internal HttpRequestMessage CreateRequest(HttpMethod method, string path, string accept)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
        request.Headers.TryAddWithoutValidation("Authorization", _node.Endpoint.Password);
        request.Headers.TryAddWithoutValidation("User-Agent", ClientHeaders.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        return request;
    }

    internal static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var value = response.Headers.RetryAfter;
        if (value is null)
            return null;
        if (value.Delta is { } delta)
            return delta;
        if (value.Date is { } date)
        {
            var span = date - DateTimeOffset.UtcNow;
            return span > TimeSpan.Zero ? span : null;
        }
        return null;
    }
}
