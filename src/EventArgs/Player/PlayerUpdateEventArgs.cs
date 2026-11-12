using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The server reported the state of the player.</summary>
public sealed class PlayerUpdateEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    private PlayerState? _state;

    /// <summary>The state the server reported.</summary>
    /// <remarks>Never null, a missing value reads as an empty instance.</remarks>
    [JsonPropertyName("state"), JsonInclude]
    public PlayerState State
    {
        get => _state ??= new();
        internal set => _state = value;
    }

    internal bool StateCarried => _state is not null;
}
