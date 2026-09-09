using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

public sealed class PlayerCreatedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    /// <summary>The player state as the server created it.</summary>
    [JsonPropertyName("player")]
    public Player Player { get; init; } = new();
}
