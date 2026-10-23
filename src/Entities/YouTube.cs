using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>The runtime YouTube configuration of a NodeLink server, as the config route reports it.</summary>
/// <remarks>Tokens come back masked, the route never hands out the stored secrets.</remarks>
public sealed record YouTubeConfig
{
    /// <summary>The masked OAuth refresh token, null when none is configured.</summary>
    [JsonPropertyName("refreshToken"), JsonInclude]
    public string? RefreshToken { get; internal set; }

    /// <summary>The masked visitor data, null when none is configured.</summary>
    [JsonPropertyName("visitorData"), JsonInclude]
    public string? VisitorData { get; internal set; }

    /// <summary>Whether a refresh token is currently configured.</summary>
    [JsonPropertyName("isConfigured"), JsonInclude]
    public bool IsConfigured { get; internal set; }

    /// <summary>
    /// Whether the configured token still validates, only reported when the read ran with <c>validate</c>,
    /// null otherwise.
    /// </summary>
    [JsonPropertyName("isValid"), JsonInclude]
    public bool? IsValid { get; internal set; }
}

/// <summary>The outcome of a YouTube configuration update on a NodeLink server.</summary>
public sealed record YouTubeConfigUpdate
{
    /// <summary>The human-readable confirmation of the update.</summary>
    [JsonPropertyName("message"), JsonInclude]
    public string Message
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>The number of workers the change was propagated to, as reported by the server.</summary>
    [JsonPropertyName("workersUpdated"), JsonInclude]
    public int WorkersUpdated { get; internal set; }

    /// <summary>The fields the patch asked the server to change, as the server reports them.</summary>
    [JsonPropertyName("fieldsUpdated"), JsonInclude]
    public IReadOnlyList<string> FieldsUpdated
    {
        get;
        internal set => field = value is null ? [] : [.. value.Where(f => f is not null)];
    } = [];

    /// <summary>Indicates whether the updates carry the same members.</summary>
    /// <param name="other">The update to compare with.</param>
    /// <returns>Whether the members match, the fields compared by content.</returns>
    public bool Equals(YouTubeConfigUpdate? other) =>
        other is not null
        && Message == other.Message
        && WorkersUpdated == other.WorkersUpdated
        && ContentEquality.ListEquals(FieldsUpdated, other.FieldsUpdated);

    /// <summary>Returns a hash of the update's comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Message, WorkersUpdated);
}

/// <summary>An access token the YouTube OAuth route issued for a refresh token.</summary>
/// <remarks>
/// The relay echoes the token endpoint's answer verbatim, and only the four modeled members survive the parse,
/// anything else the provider appends (e.g. a rotated refresh token) is dropped.
/// </remarks>
public sealed record YouTubeToken
{
    /// <summary>The issued access token.</summary>
    [JsonPropertyName("access_token"), JsonInclude]
    public string AccessToken
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>How long the token stays valid, in seconds.</summary>
    [JsonPropertyName("expires_in"), JsonInclude]
    public long ExpiresInSeconds { get; internal set; }

    /// <summary>The scopes the token grants.</summary>
    [JsonPropertyName("scope"), JsonInclude]
    public string? Scope { get; internal set; }

    /// <summary>The token type, e.g. <c>Bearer</c>.</summary>
    [JsonPropertyName("token_type"), JsonInclude]
    public string? TokenType { get; internal set; }
}

internal sealed record YouTubeTokenRefresh(
    [property: JsonPropertyName("refreshToken")] string RefreshToken
)
{
    public override string ToString() => "YouTubeTokenRefresh { RefreshToken = ***, elided }";
}

internal sealed record YouTubeConfigPatch(
    [property: JsonPropertyName("refreshToken")] string? RefreshToken = null,
    [property: JsonPropertyName("visitorData")] string? VisitorData = null
);
