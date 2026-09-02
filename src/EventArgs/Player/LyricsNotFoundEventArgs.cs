using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

public sealed class LyricsNotFoundEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }
}
