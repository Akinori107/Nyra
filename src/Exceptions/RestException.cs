using System.Globalization;

namespace Nyra.Exceptions;

/// <summary>
/// A failed REST request against an audio node, carries the status code and error body when available.
/// </summary>
public sealed class RestException : NyraException
{
    /// <summary>The HTTP status the server replied with.</summary>
    /// <remarks>
    /// A failure raised on the client side (a body that did not parse) carries a synthetic 2xx, standing for
    /// the reply the server gave before its body failed the read. Only a real server reply carries its own
    /// status, including the route planner's empty 204, a success reported through the same type.
    /// </remarks>
    public int StatusCode { get; }

    /// <summary>The body the server replied with, null when it sent none.</summary>
    /// <remarks>
    /// A client-side parse failure carries the client's diagnostic instead of the body, a reply the server
    /// null-ed reads as null, the diagnostic is what the client kept. A body at or beyond the read bound is
    /// carried truncated with a marker.
    /// </remarks>
    public string? ResponseBody { get; }

    /// <summary>The path the failed request went to.</summary>
    public string Path { get; }

    /// <summary>The retry delay the server asked for, null when it sent none.</summary>
    /// <remarks>
    /// NodeLink's admission control answers 429 with this header, so the caller knows how long to wait before
    /// retrying.
    /// </remarks>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Creates the exception.</summary>
    /// <param name="statusCode">The HTTP status the server replied with.</param>
    /// <param name="path">The path the failed request went to.</param>
    /// <param name="responseBody">The body the server replied with, null when it sent none.</param>
    /// <param name="retryAfter">The retry delay the server asked for, null when it sent none.</param>
    /// <param name="innerException">The exception that caused this one, null when there is none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public RestException(
        int statusCode,
        string path,
        string? responseBody,
        TimeSpan? retryAfter = null,
        Exception? innerException = null
    )
        : base(BuildMessage(statusCode, path, responseBody, retryAfter), innerException)
    {
        StatusCode = statusCode;
        Path = path;
        ResponseBody = responseBody;
        RetryAfter = retryAfter;
    }

    private static string BuildMessage(
        int statusCode,
        string path,
        string? responseBody,
        TimeSpan? retryAfter
    )
    {
        ArgumentNullException.ThrowIfNull(path);

        var statusText = Enum.IsDefined((System.Net.HttpStatusCode)statusCode)
            ? $"{(System.Net.HttpStatusCode)statusCode} ({statusCode})"
            : $"{statusCode}";
        var message = $"The audio node responded with {statusText} for '{SummarizePath(path)}'.";
        if (retryAfter is { } delay)
        {
            var retrySeconds = delay.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
            message += $" Retry-After: {retrySeconds}s.";
        }
        if (!string.IsNullOrWhiteSpace(responseBody))
            message += $" Response: {Summarize(responseBody)}";
        return message;
    }

    private static string SummarizePath(string path)
    {
        var query = path.IndexOf('?');
        int cut;
        if (query >= 0 && path.Length - query > 64)
            cut = query + 32;
        else if (path.Length > 96)
            cut = 64;
        else
            return path;
        if (char.IsHighSurrogate(path[cut - 1]))
            cut--;
        return path[..cut] + "...";
    }

    private static string Summarize(string body)
    {
        var folded = body.Trim().ReplaceLineEndings(" ");
        if (folded.Length <= 512)
            return folded;

        var cut = char.IsHighSurrogate(folded[511]) ? 511 : 512;
        return $"{folded[..cut]}...";
    }
}
