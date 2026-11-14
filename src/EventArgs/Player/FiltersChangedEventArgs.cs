using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>Raised when the server finished applying a filter update.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed class FiltersChangedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The filters the server reports as applied afterwards.</summary>
    /// <remarks>
    /// Never null, a missing value reads as an empty chain. Sending an empty chain back is not a no-op.
    /// </remarks>
    [JsonPropertyName("filters"), JsonInclude]
    public FilterChain Filters
    {
        get => field ??= new();
        internal set;
    }
}
