using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The EternalBox engine moved to another beat of the playing track.</summary>
public sealed class EternalBoxJumpedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    /// <summary>The EternalBox track being played.</summary>
    [JsonPropertyName("track")]
    public Track? Track { get; init; }

    [JsonPropertyName("eternalbox")]
    public EternalBoxJump? Jump { get; init; }
}
