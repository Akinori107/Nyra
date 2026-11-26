using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>
/// A NodeLink cluster worker crashed and took the players of some guilds with it. Their guild connections
/// are invalidated.
/// </summary>
/// <remarks>NodeLink only.</remarks>
public sealed class WorkerFailedEventArgs : System.EventArgs
{
    /// <summary>The guilds whose players died with the worker, parsed into their numeric ids.</summary>
    /// <remarks>
    /// Ids that cannot be read are skipped.
    /// </remarks>
    [JsonPropertyName("affectedGuilds"), JsonInclude]
    [JsonConverter(typeof(SnowflakeListConverter))]
    public IReadOnlyList<ulong>? AffectedGuilds { get; internal set; }

    /// <summary>
    /// The message the server sent about the crash.
    /// </summary>
    [JsonPropertyName("message"), JsonInclude]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string? Message { get; internal set; }
}
