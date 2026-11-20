using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>
/// EternalBox context of the playing track, reported when the track starts.
/// </summary>
/// <remarks>NodeLink only.</remarks>
public sealed class EternalBoxInfoUpdatedEventArgs : System.EventArgs
{
    /// <summary>The guild the player plays in.</summary>
    [JsonPropertyName("guildId"), JsonInclude]
    [JsonConverter(typeof(SnowflakeConverter))]
    public ulong GuildId { get; internal set; }

    /// <summary>The track the info belongs to, always the track the event fired for.</summary>
    [JsonPropertyName("track"), JsonInclude]
    public Track? Track { get; internal set; }

    /// <summary>The info the provider reported for the track.</summary>
    /// <remarks>Never null, a missing value reads as an empty instance.</remarks>
    [JsonPropertyName("eternalbox"), JsonInclude]
    public EternalBoxInfo Info
    {
        get;
        internal set => field = value ?? new();
    } = new();
}
