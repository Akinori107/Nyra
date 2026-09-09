namespace Nyra;

/// <summary>Network endpoint of an audio node, both servers serve REST and websocket on the same host and port.</summary>
public sealed class Endpoint
{
    public string Host { get; }

    public int Port { get; }

    public string Password { get; }

    /// <summary>Whether the endpoint is served over TLS.</summary>
    public bool IsSecure { get; }

    public Endpoint(string host, int port, string password, bool isSecure = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        // Anything but a bare hostname or IP literal would silently build a broken URI (the
        // scheme comes from isSecure, so a URL here corrupts the address), bracketed IPv6
        // literals are the one form allowed to contain colons.
        var isIpv6Literal = host.StartsWith('[') && host.EndsWith(']');
        foreach (var character in host)
            if (
                character is '/' or '?' or '#' or ':' or '@' && !isIpv6Literal
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

    public Uri ToRestUri() =>
        new($"http{(IsSecure ? "s" : string.Empty)}://{Host}:{Port}/", UriKind.Absolute);

    public Uri ToWebSocketUri() =>
        new(
            $"ws{(IsSecure ? "s" : string.Empty)}://{Host}:{Port}{Endpoints.WebSocketPath}",
            UriKind.Absolute
        );

    public override string ToString() => $"{Host}:{Port}";
}
