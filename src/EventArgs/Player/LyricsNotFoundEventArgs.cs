using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>
/// No timed lyrics for the track, no provider had any, or the provider returned text-only lyrics (LavaLyrics).
/// </summary>
/// <remarks>
/// NodeLink answers it when nothing was found or the lyrics load failed (its REST surface reports the same
/// case as an error).
/// </remarks>
public sealed class LyricsNotFoundEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }
}
