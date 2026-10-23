using Nyra.Internal;

namespace Nyra;

/// <summary>
/// Network endpoint of an audio node, both servers serve REST and websocket on the same host and port.
/// </summary>
public sealed class Endpoint
{
    /// <summary>The host of the node, a bare hostname or IP address, IPv6 literals wrapped in brackets.</summary>
    public string Host { get; }

    /// <summary>The port of the node.</summary>
    public int Port { get; }

    /// <summary>The password used to authenticate with the node.</summary>
    public string Password { get; }

    /// <summary>Whether the endpoint is served over TLS.</summary>
    public bool IsSecure { get; }

    /// <summary>Creates an endpoint.</summary>
    /// <param name="host">
    /// The host of the node, a bare hostname or IP address. Wrap IPv6 address literals in brackets.
    /// </param>
    /// <param name="port">The port of the node.</param>
    /// <param name="password">The password used to authenticate with the node.</param>
    /// <param name="isSecure">Whether the endpoint is served over TLS.</param>
    /// <exception cref="ArgumentException">
    /// The host or the password is null or blank, or the host is not a bare hostname or IP address.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The port is outside the valid range.</exception>
    public Endpoint(string host, int port, string password, bool isSecure = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var isIpv6Literal = host.StartsWith('[') && host.EndsWith(']');
        foreach (var character in host)
            if (
                (character is '/' or '?' or '#' or ':' or '@' && !isIpv6Literal)
                || char.IsWhiteSpace(character)
            )
                throw new ArgumentException(
                    "The host must be a bare hostname or IP address, not a URL, pass the scheme through isSecure.",
                    nameof(host)
                );

        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        Host = host;
        Port = port;
        Password = password;
        IsSecure = isSecure;
    }

    /// <summary>The base URI of the node's REST API.</summary>
    /// <returns>The base REST URI of the endpoint.</returns>
    public Uri ToRestUri() =>
        new($"http{(IsSecure ? "s" : string.Empty)}://{Host}:{Port}/", UriKind.Absolute);

    /// <summary>The URI of the node's websocket.</summary>
    /// <returns>The websocket URI of the endpoint.</returns>
    public Uri ToWebSocketUri() =>
        new(
            $"ws{(IsSecure ? "s" : string.Empty)}://{Host}:{Port}{Routes.WebSocketPath}",
            UriKind.Absolute
        );

    /// <summary>Returns the endpoint in host and port form.</summary>
    /// <returns>The endpoint in host and port form.</returns>
    public override string ToString() => $"{Host}:{Port}";
}
