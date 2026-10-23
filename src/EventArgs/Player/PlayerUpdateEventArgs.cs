using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The server reported the state of the player.</summary>
public sealed class PlayerUpdateEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    private PlayerState? _state;

    /// <summary>The state the server reported.</summary>
    /// <remarks>
    /// Absorbs a null or missing state, the getter materializes a fresh instance, so handlers never read null
    /// but cannot tell a fabricated empty state from a server-reported one.
    /// </remarks>
    [JsonPropertyName("state"), JsonInclude]
    public PlayerState State
    {
        get => _state ??= new();
        internal set => _state = value;
    }

    internal bool StateCarried => _state is not null;
}
