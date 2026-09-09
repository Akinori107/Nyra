using System.Text.Json.Serialization;

namespace Nyra.EventArgs;

public sealed class VolumeChangedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    /// <summary>New volume, 0-1000, where 100 is the default.</summary>
    [JsonPropertyName("volume")]
    public int Volume { get; init; }
}
