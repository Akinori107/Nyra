using System.Globalization;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nyra.Exceptions;
using Nyra.WebSocket;

namespace Nyra.Internal;

internal static partial class ConnectionFailure
{
    [GeneratedRegex(@"status code '(\d{3})'")]
    private static partial Regex HandshakeStatusCodeRegex();

    public static void Log(
        ILogger? logger,
        string nodeName,
        Endpoint endpoint,
        bool initial,
        Exception exception
    )
    {
        var endpointText = endpoint.ToString();
        try
        {
            switch (exception)
            {
                case HttpRequestException { InnerException: AuthenticationException }:
                    logger?.LogWarning(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) rejected the connection: TLS handshake failed. Check that IsSecure isn't set to true for a node that isn't TLS, a certificate that expired or is not trusted also lands here when IsSecure is correct.",
                        nodeName,
                        endpointText
                    );
                    break;
                case WebSocketException { InnerException: HttpRequestException httpException }
                    when httpException.InnerException is AuthenticationException:
                    logger?.LogWarning(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) rejected the connection: TLS handshake failed. Check that IsSecure matches the node's port, a certificate that expired since the last successful connect also lands here.",
                        nodeName,
                        endpointText
                    );
                    break;
                case TimeoutException timeoutException:
                    logger?.LogWarning(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) not reachable: connection timed out: {Reason}.",
                        nodeName,
                        endpointText,
                        TrimTrailingPeriod(timeoutException.Message)
                    );
                    break;
                case TaskCanceledException { InnerException: TimeoutException }:
                    logger?.LogWarning(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) not reachable: connection timed out.",
                        nodeName,
                        endpointText
                    );
                    break;
                case HttpRequestException requestException:
                    logger?.LogWarning(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) not reachable: {Reason}.",
                        nodeName,
                        endpointText,
                        Describe(requestException)
                    );
                    break;
                case NodeUnreachableException unreachableException:
                    logger?.LogWarning(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) not reachable: {Reason}.",
                        nodeName,
                        endpointText,
                        unreachableException.InnerException is { } transport
                            ? Describe(transport)
                            : TrimTrailingPeriod(unreachableException.Message)
                    );
                    break;
                case WebSocketException { InnerException: HttpRequestException httpException }:
                    logger?.LogWarning(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) not reachable: {Reason}.",
                        nodeName,
                        endpointText,
                        Describe(httpException)
                    );
                    break;
                case not null
                    when TryGetStatusCode(exception, out var authStatus)
                        && authStatus is 401 or 403:
                    logger?.LogWarning(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) rejected the connection: check the node's configured password.",
                        nodeName,
                        endpointText
                    );
                    break;
                case WebSocketException webSocketException
                    when TryGetHandshakeStatusCode(webSocketException, out var statusCode)
                        && statusCode is 400:
                    logger?.LogWarning(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) rejected the connection with HTTP 400. Check the bridge's user id, NodeLink rejects user ids that are not snowflakes.",
                        nodeName,
                        endpointText
                    );
                    break;
                case RestException { StatusCode: 400, ResponseBody: var httpsBody }
                    when httpsBody?.Contains(
                        "sent to HTTPS port",
                        StringComparison.OrdinalIgnoreCase
                    ) == true:
                    logger?.LogWarning(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) rejected the connection: {Reason}. Check whether the node's IsSecure should be true.",
                        nodeName,
                        endpointText,
                        "HTTP request sent to HTTPS port"
                    );
                    break;
                case not null
                    when TryGetStatusCode(exception, out var proxyStatus)
                        && proxyStatus is 502 or 504 or (>= 520 and <= 526) or 529 or 530:
                    logger?.LogWarning(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) not reachable: reverse proxy in front of it returned {StatusCode}.",
                        nodeName,
                        endpointText,
                        proxyStatus
                    );
                    break;
                case RestException or IncompatibleServerException:
                    logger?.LogWarning(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) rejected the connection: {Reason}.",
                        nodeName,
                        endpointText,
                        TrimTrailingPeriod(exception.Message)
                    );
                    break;
                case ArgumentException:
                    logger?.LogWarning(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) rejected the connection: {Reason}. Check the session id the server handed out and any user-supplied header values.",
                        nodeName,
                        endpointText,
                        TrimTrailingPeriod(exception.Message)
                    );
                    break;
                case OperationCanceledException:
                    logger?.LogInformation(
                        LogEvents.Node,
                        "Node {NodeName} ({Endpoint}) connection attempt cancelled.",
                        nodeName,
                        endpointText
                    );
                    break;
                default:
                    logger?.LogError(
                        LogEvents.Node,
                        exception,
                        "Node {NodeName} ({Endpoint}) {Failure}.",
                        nodeName,
                        endpointText,
                        initial ? "failed to connect unexpectedly" : "failed to reconnect"
                    );
                    break;
            }
        }
        catch { }
    }

    private static bool TryGetStatusCode(Exception exception, out int statusCode)
    {
        if (exception is RestException rest)
        {
            statusCode = rest.StatusCode;
            return true;
        }

        if (exception is WebSocketException webSocket)
            return TryGetHandshakeStatusCode(webSocket, out statusCode);

        statusCode = 0;
        return false;
    }

    private static bool TryGetHandshakeStatusCode(WebSocketException exception, out int statusCode)
    {
        if (exception.Data[WebSocketClient.HandshakeStatusCodeKey] is int structured)
        {
            statusCode = structured;
            return true;
        }

        var match = HandshakeStatusCodeRegex().Match(exception.Message);
        if (!match.Success)
        {
            statusCode = default;
            return false;
        }

        statusCode = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return true;
    }

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

    private static string StripAddressSuffix(string message)
    {
        var index = message.LastIndexOf(" (", StringComparison.Ordinal);
        return index >= 0 && message.EndsWith(')') ? message[..index] : message;
    }

    private static string TrimTrailingPeriod(string message) =>
        message.EndsWith('.') ? message[..^1] : message;
}
