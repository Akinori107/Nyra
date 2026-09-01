using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Why a mix layer ended.</summary>
public enum MixEndReason
{
    /// <summary>Played through to the end; only fires when auto-cleanup is on.</summary>
    [JsonPropertyName("FINISHED")]
    Finished,

    /// <summary>Stopped manually, e.g. via <see cref="GuildConnection.StopMixAsync"/>.</summary>
    [JsonPropertyName("REMOVED")]
    Removed,

    [JsonPropertyName("ERROR")]
    Error,

    /// <summary>The main track ended; only fires when auto-cleanup is on.</summary>
    [JsonPropertyName("MAIN_ENDED")]
    MainEnded,

    [JsonPropertyName("DESTROYED")]
    Destroyed,

    /// <summary>A reason the library does not know, reported by a newer server.</summary>
    [JsonPropertyName("unknown")]
    Unknown,
}

/// <summary>One mix layer of a player: a track playing on top of the main one.</summary>
public sealed record MixLayer
{
    /// <summary>The layer's identifier, used to change or stop it.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("track")]
    public Track Track { get; init; } = new();

    /// <summary>Layer loudness, 0.0-1.0.</summary>
    [JsonPropertyName("volume")]
    public float Volume { get; init; }

    /// <summary>Playback position, in milliseconds. Only set by <see cref="GuildConnection.GetMixLayersAsync"/>; null right after creation.</summary>
    [JsonPropertyName("position")]
    public long? PositionMilliseconds { get; init; }

    /// <summary>Unix timestamp of when the layer started, in milliseconds. Only set by <see cref="GuildConnection.GetMixLayersAsync"/>; null right after creation.</summary>
    [JsonPropertyName("startTime")]
    public long? StartTimeMilliseconds { get; init; }
}

/// <summary>Request body of the mix-layer create endpoint.</summary>
internal sealed record MixLayerCreate(
    [property: JsonPropertyName("track")] PlayerUpdateTrack Track,
    [property: JsonPropertyName("volume")] float? Volume = null
);

/// <summary>Request body of the mix-layer volume endpoint.</summary>
internal sealed record MixLayerVolume([property: JsonPropertyName("volume")] float Volume);

/// <summary>Response body of the mix-layer list endpoint.</summary>
internal sealed record MixLayerCollection(
    [property: JsonPropertyName("mixes")] IReadOnlyList<MixLayer> Mixes
);
