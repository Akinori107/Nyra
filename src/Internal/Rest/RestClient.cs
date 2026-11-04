using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Nyra.Exceptions;

namespace Nyra.Internal;

internal sealed class RestClient
{
    internal const int MaxBodyBytes = 64 * 1024 * 1024;

    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(10),
    };

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private static readonly HttpClient Http = new(SharedHandler, disposeHandler: false)
    {
        Timeout = Timeout.InfiniteTimeSpan,
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

    internal static bool IndicatesMissingResource(RestException exception) =>
        exception.StatusCode == 404
        && !exception.BodyUnreadable
        && !IndicatesMissingSession(exception);

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
        try
        {
            var reply = await SendCoreAsync(method, path, jsonBody, cancellationToken)
                .ConfigureAwait(false);
            _node.NoteRestSuccess();
            return reply;
        }
        catch (Exception exception)
            when (exception
                    is TimeoutException
                        or NodeUnreachableException
                        or RestException { StatusCode: 429 or >= 500 }
            )
        {
            _node.NoteRestFailure();
            throw;
        }
    }

    private async Task<(int Status, byte[] Body)> SendCoreAsync(
        HttpMethod method,
        string path,
        byte[]? jsonBody,
        CancellationToken cancellationToken
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
            HttpResponseMessage reply;
            using (var headerCts = new TimeoutLink(_node.Time, RequestTimeout, cancellationToken))
                reply = await Http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        headerCts.Token
                    )
                    .ConfigureAwait(false);
            using var response = reply;

            using var bodyCts = new TimeoutLink(_node.Time, RequestTimeout, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var retryAfter = ReadRetryAfter(response);
                var (body, bodyUnreadable) = await ReadErrorBodyAsync(
                        response.Content,
                        bodyCts.Token
                    )
                    .ConfigureAwait(false);
                throw new RestException((int)response.StatusCode, path, body, retryAfter)
                {
                    BodyUnreadable = bodyUnreadable,
                };
            }

            var statusCode = (int)response.StatusCode;
            var bytes = await ReadBoundedAsync(response.Content, MaxBodyBytes, bodyCts.Token)
                .ConfigureAwait(false);
            return bytes is null
                ? throw new RestException(
                    statusCode,
                    path,
                    $"The response body is larger than the {MaxBodyBytes} byte read bound."
                )
                : (statusCode, bytes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The node '{_node.Name}' at {_node.Endpoint} did not answer the request in time, the client's own bound fired and the caller's token is intact."
            );
        }
        catch (OperationCanceledException exception)
            when (exception.CancellationToken != cancellationToken)
        {
            throw new OperationCanceledException(exception.Message, exception, cancellationToken);
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

    internal static async Task<(string Body, bool Unreadable)> ReadErrorBodyAsync(
        HttpContent content,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await content
                .LoadIntoBufferAsync(MaxBodyBytes, cancellationToken)
                .ConfigureAwait(false);
            return (
                await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
                false
            );
        }
        catch (Exception readFailure) when (readFailure is not OperationCanceledException)
        {
            return (string.Empty, true);
        }
    }

    internal static async Task<byte[]?> ReadBoundedAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken
    )
    {
        var declared = content.Headers.ContentLength;
        if (declared > maxBytes)
            return null;

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream((int)(declared ?? 0));
            var chunk = ArrayPool<byte>.Shared.Rent(16 * 1024);
            try
            {
                int read;
                while (
                    (read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false))
                    > 0
                )
                {
                    if (buffer.Length + read > maxBytes)
                        return null;
                    buffer.Write(chunk, 0, read);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(chunk);
            }

            return buffer.ToArray();
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
            return delta < MaxRetryAfter ? delta : MaxRetryAfter;
        if (value.Date is { } date)
        {
            var span = date - DateTimeOffset.UtcNow;
            return span > TimeSpan.Zero ? (span < MaxRetryAfter ? span : MaxRetryAfter) : null;
        }
        return null;
    }
}
