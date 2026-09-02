using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

public sealed class MixEndedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    [JsonPropertyName("mixId")]
    public string MixId { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public MixEndReason EndReason { get; init; }
}
