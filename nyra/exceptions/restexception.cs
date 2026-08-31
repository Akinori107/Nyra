namespace Nyra.Exceptions;

/// <summary>A failed REST request against an audio node; carries the status code and error body when available.</summary>
public sealed class RestException : NyraException
{
    public int StatusCode { get; }

    public string? ResponseBody { get; }

    public string? Path { get; }

    public RestException(int statusCode, string? path, string? responseBody)
        : base(BuildMessage(statusCode, path, responseBody))
    {
        StatusCode = statusCode;
        Path = path;
        ResponseBody = responseBody;
    }

    private static string BuildMessage(int statusCode, string? path, string? responseBody)
    {
        var message =
            $"The audio node responded with {(System.Net.HttpStatusCode)statusCode} ({statusCode}) for '{path ?? "unknown path"}'.";
        if (!string.IsNullOrWhiteSpace(responseBody))
            message += $" Response: {Summarize(responseBody)}";
        return message;
    }

    private static string Summarize(string body)
    {
        var folded = body.Trim().ReplaceLineEndings(" ");
        return folded.Length <= 512 ? folded : $"{folded[..512]}...";
    }
}
