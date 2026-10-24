using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The server failed to load or play the current track.</summary>
public sealed class TrackExceptionEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>
    /// The track the exception occurred on, null when the player has no track. NodeLink reports it after the
    /// track already ended, or when the voice reconnection circuit breaker fires before any track loaded (a
    /// voice-level failure, not a track failure).
    /// </summary>
    [JsonPropertyName("track"), JsonInclude]
    public Track? Track { get; internal set; }

    /// <summary>
    /// The error the server reported, the server's own wire field. Not a local handler failure, exceptions
    /// thrown by this client's own handlers arrive as <see cref="HandlerErrorEventArgs.Exception"/>
    /// instead.
    /// </summary>
    /// <remarks>
    /// Absorbs a null error the server sends, the setter substitutes a fresh instance, so handlers never read
    /// null even when the frame carries one.
    /// </remarks>
    [JsonPropertyName("exception"), JsonInclude]
    public ErrorInfo Exception
    {
        get;
        internal set => field = value ?? new();
    } = new();
}
