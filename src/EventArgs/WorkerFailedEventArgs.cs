using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>
/// A NodeLink cluster worker crashed and the players of the affected guilds are gone, the worker respawns
/// empty, players are only recreated on the next client request, and the affected guild connections are
/// invalidated.
/// </summary>
/// <remarks>NodeLink only, cluster mode is on by default.</remarks>
public sealed class WorkerFailedEventArgs : System.EventArgs
{
    /// <summary>The guilds whose players died with the worker, parsed into their numeric ids.</summary>
    /// <remarks>
    /// A null, malformed or absent member reads as empty, so does a null or garbage element, one bad id must
    /// not take the whole invalidation down with it.
    /// </remarks>
    [JsonPropertyName("affectedGuilds"), JsonInclude]
    [JsonConverter(typeof(SnowflakeListConverter))]
    public IReadOnlyList<ulong>? AffectedGuilds { get; internal set; }

    /// <summary>
    /// The summary the cluster master reported when it rebuilt the failure notification, always present in
    /// NodeLink's frames, null only when a hostile frame carried no message.
    /// </summary>
    /// <remarks>A number reads as its text, a crashed worker can report a bare code.</remarks>
    [JsonPropertyName("message"), JsonInclude]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string? Message { get; internal set; }
}
