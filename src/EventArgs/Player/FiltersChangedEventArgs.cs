using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

public sealed class FiltersChangedEventArgs : System.EventArgs
{
    [JsonPropertyName("guildId")]
    public ulong GuildId { get; init; }

    /// <summary>The filters the server reports as applied afterwards.</summary>
    [JsonPropertyName("filters")]
    public FilterChain? Filters { get; init; }
}
