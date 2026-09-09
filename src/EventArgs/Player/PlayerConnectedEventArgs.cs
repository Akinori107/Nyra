using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The voice connection of the guild's player became established.</summary>
public sealed class PlayerConnectedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    /// <summary>Voice session details at the moment of the connection.</summary>
    [JsonPropertyName("voice")]
    public VoiceState? Voice { get; init; }
}
