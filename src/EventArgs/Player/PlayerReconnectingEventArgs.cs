using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The voice connection of the guild's player dropped and is being re-established.</summary>
public sealed class PlayerReconnectingEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    /// <summary>Voice session details at the moment the reconnection began.</summary>
    [JsonPropertyName("voice")]
    public VoiceState? Voice { get; init; }
}
