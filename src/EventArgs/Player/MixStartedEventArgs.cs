using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

public sealed class MixStartedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    [JsonPropertyName("mixId")]
    public string MixId { get; init; } = string.Empty;

    [JsonPropertyName("track")]
    public Track Track { get; init; } = new();

    [JsonPropertyName("volume")]
    public float Volume { get; init; }
}
