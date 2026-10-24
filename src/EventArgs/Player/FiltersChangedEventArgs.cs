using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>Raised when the server finished applying a filter update.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed class FiltersChangedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(TolerantSnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The filters the server reports as applied afterwards.</summary>
    /// <remarks>
    /// Absorbs a null the server sends, the getter never hands out null, a missing or nulled chain reads as an
    /// empty one, <see cref="Player.Filters"/> on the read models keeps its own null contract. An empty chain
    /// re-sent writes every filter's neutral defaults, which are not all no-ops.
    /// </remarks>
    [JsonPropertyName("filters"), JsonInclude]
    public FilterChain Filters
    {
        get => _filters ??= new();
        internal set => _filters = value;
    }

    private FilterChain? _filters;
}
