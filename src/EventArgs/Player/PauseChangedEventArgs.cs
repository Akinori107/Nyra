using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>Raised on both pausing and resuming, <see cref="IsPaused"/> tells them apart.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed class PauseChangedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>Whether playback is paused now.</summary>
    /// <remarks>A frame without the member is rejected, not read as a resume.</remarks>
    [JsonPropertyName("paused"), JsonInclude]
    [JsonRequired]
    public bool IsPaused { get; internal set; }
}
