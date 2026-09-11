using System.Globalization;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nyra.Exceptions;

namespace Nyra.Internal;

/// <summary>Turns connection-attempt failures into log messages, shared by <see cref="NyraExtension"/> and <see cref="NodeConnection"/>.</summary>
internal static partial class ConnectionFailure
{
    // ClientWebSocket doesn't expose the handshake's HTTP status code as a structured
    // property, it's only present in the exception message text.
    [GeneratedRegex(@"status code '(\d{3})'")]
    private static partial Regex HandshakeStatusCodeRegex();

    /// <summary>Logs one failed connection attempt: a recognized cause becomes a warning with a hint at the likely misconfiguration, anything else becomes an error.</summary>
    /// <param name="logger">The logger to write to, a null logger is a no-op.</param>
    /// <param name="nodeName">The name of the node whose connection attempt failed.</param>
    /// <param name="endpoint">The endpoint of the node whose connection attempt failed, always logged alongside the name so a multi-node setup can tell which host failed.</param>
    /// <param name="initial">Whether this is the startup connect rather than a reconnect attempt, which only changes the wording.</param>
    /// <param name="exception">The exception raised by the failed connection attempt.</param>
    public static void Log(
        ILogger? logger,
        string nodeName,
        Endpoint endpoint,
        bool initial,
        Exception exception
    )
    {
        var endpointText = endpoint.ToString();
        switch (exception)
        {
            case HttpRequestException { InnerException: AuthenticationException }:
                logger?.LogWarning(
                    "Node {NodeName} ({Endpoint}) rejected the connection: TLS handshake failed. Check that IsSecure isn't set to true for a node that isn't TLS.",
                    nodeName,
                    endpointText
                );
                break;
            // HttpClient's ConnectTimeout, not a caller cancellation.
            case TaskCanceledException { InnerException: TimeoutException }:
                logger?.LogWarning(
                    "Node {NodeName} ({Endpoint}) not reachable: connection timed out.",
                    nodeName,
                    endpointText
                );
                break;
            case HttpRequestException requestException:
                logger?.LogWarning(
                    "Node {NodeName} ({Endpoint}) not reachable: {Reason}.",
                    nodeName,
                    endpointText,
                    Describe(requestException)
                );
                break;
            case WebSocketException { InnerException: HttpRequestException httpException }:
                logger?.LogWarning(
                    "Node {NodeName} ({Endpoint}) not reachable: {Reason}.",
                    nodeName,
                    endpointText,
                    Describe(httpException)
                );
                break;
            case RestException { StatusCode: 401 or 403 }:
                logger?.LogWarning(
                    "Node {NodeName} ({Endpoint}) rejected the connection: check the node's configured password.",
                    nodeName,
                    endpointText
                );
                break;
            case WebSocketException webSocketException
                when TryGetHandshakeStatusCode(webSocketException, out var statusCode)
                    && statusCode is 401 or 403:
                logger?.LogWarning(
                    "Node {NodeName} ({Endpoint}) rejected the connection: check the node's configured password.",
                    nodeName,
                    endpointText
                );
                break;
            case WebSocketException webSocketException
                when TryGetHandshakeStatusCode(webSocketException, out var statusCode)
                    && statusCode is 502 or 504 or (>= 520 and <= 526) or 529 or 530:
                logger?.LogWarning(
                    "Node {NodeName} ({Endpoint}) not reachable: reverse proxy in front of it returned {StatusCode}.",
                    nodeName,
                    endpointText,
                    statusCode
                );
                break;
            // Reverse proxies commonly return this exact text when the port expects HTTPS but IsSecure is off.
            case RestException { StatusCode: 400, ResponseBody: var httpsBody }
                when httpsBody?.Contains("sent to HTTPS port", StringComparison.OrdinalIgnoreCase)
                    == true:
                logger?.LogWarning(
                    "Node {NodeName} ({Endpoint}) rejected the connection: {Reason}. Check whether the node's IsSecure should be true.",
                    nodeName,
                    endpointText,
                    "HTTP request sent to HTTPS port"
                );
                break;
            case RestException restException:
                logger?.LogWarning(
                    "Node {NodeName} ({Endpoint}) rejected the connection: {Reason}.",
                    nodeName,
                    endpointText,
                    TrimTrailingPeriod(restException.Message)
                );
                break;
            case IncompatibleServerException incompatibleException:
                logger?.LogWarning(
                    "Node {NodeName} ({Endpoint}) rejected the connection: {Reason}.",
                    nodeName,
                    endpointText,
                    TrimTrailingPeriod(incompatibleException.Message)
                );
                break;
            default:
                logger?.LogError(
                    exception,
                    "Node {NodeName} ({Endpoint}) {Failure}.",
                    nodeName,
                    endpointText,
                    initial ? "failed to connect unexpectedly" : "failed to reconnect"
                );
                break;
        }
    }

    /// <summary>Extracts the HTTP status code from a failed WebSocket handshake, if present.</summary>
    private static bool TryGetHandshakeStatusCode(WebSocketException exception, out int statusCode)
    {
        var match = HandshakeStatusCodeRegex().Match(exception.Message);
        if (!match.Success)
        {
            statusCode = default;
            return false;
        }

        statusCode = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Describes an <see cref="HttpRequestException"/> raised while connecting to a node.</summary>
    private static string Describe(Exception exception)
    {
        if (
            exception.InnerException is SocketException
            {
                SocketErrorCode: SocketError.HostNotFound
            }
        )
            return "host or port is invalid";

        var message = StripAddressSuffix(exception.Message);
        return exception.InnerException is { } inner && !message.Contains(inner.Message)
            ? $"{message} {inner.Message}"
            : message;
    }

    // .NET appends "(host:port)" to DNS/socket failure messages, which repeats the node's own
    // address and doesn't match the rest of the log output, strip it before logging.
    private static string StripAddressSuffix(string message)
    {
        var index = message.LastIndexOf(" (", StringComparison.Ordinal);
        return index >= 0 && message.EndsWith(')') ? message[..index] : message;
    }

    // Unlike this switch's other Reason values, RestException and IncompatibleServerException
    // messages are full sentences that already end in a period, trim it so the template's own
    // trailing period doesn't double up.
    private static string TrimTrailingPeriod(string message) =>
        message.EndsWith('.') ? message[..^1] : message;
}
