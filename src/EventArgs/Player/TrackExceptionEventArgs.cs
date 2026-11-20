using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The server failed to load or play the current track.</summary>
public sealed class TrackExceptionEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>
    /// The track that failed, null when the player has no track.
    /// </summary>
    [JsonPropertyName("track"), JsonInclude]
    public Track? Track { get; internal set; }

    /// <summary>
    /// The error the server reported. Errors from your own handlers arrive as
    /// <see cref="HandlerErrorEventArgs.Exception"/>.
    /// </summary>
    /// <remarks>Never null, a missing value reads as an empty instance.</remarks>
    [JsonPropertyName("exception"), JsonInclude]
    public ErrorInfo Exception
    {
        get;
        internal set => field = value ?? new();
    } = new();
}
