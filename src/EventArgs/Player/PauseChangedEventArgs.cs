using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

/// <summary>Raised on both pausing and resuming; <see cref="IsPaused"/> tells them apart.</summary>
public sealed class PauseChangedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    [JsonPropertyName("paused")]
    public bool IsPaused { get; init; }
}
