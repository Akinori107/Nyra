using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

public sealed class PlayerDestroyedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }
}
