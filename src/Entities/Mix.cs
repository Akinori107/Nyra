using System.Globalization;
using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Why a mix layer ended.</summary>
public enum MixEndReason
{
    /// <summary>Played through to the end, only fires when auto-cleanup is on.</summary>
    Finished,

    /// <summary>Stopped manually, like <see cref="GuildMix.StopAsync"/>.</summary>
    Removed,

    /// <summary>The layer's source stream errored, the mixer removed it.</summary>
    Error,

    /// <summary>
    /// The main track ended, was stopped or was replaced, only fires when auto-cleanup is on.
    /// </summary>
    MainEnded,

    /// <summary>
    /// The mixer was torn down, player destroyed, or the main track ended/stopped, how every layer still alive
    /// ends when auto-cleanup is off.
    /// </summary>
    Destroyed,

    /// <summary>
    /// The server cleared all layers at once, like a plugin doing it.
    /// </summary>
    Cleared,

    /// <summary>A reason the library does not know, reported by a newer server.</summary>
    Unknown,
}

/// <summary>One mix layer of a player, a track playing on top of the main one.</summary>
public sealed record MixLayer
{
    /// <summary>The layer's identifier, used to change or stop it.</summary>
    [JsonPropertyName("id"), JsonInclude]
    public string Id
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>The track the layer plays.</summary>
    /// <remarks>Never null, a missing value reads as an empty instance.</remarks>
    [JsonPropertyName("track"), JsonInclude]
    public Track Track
    {
        get;
        internal set => field = value ?? new();
    } = new();

    /// <summary>Layer loudness, 0.0-1.0.</summary>
    [JsonPropertyName("volume"), JsonInclude]
    [JsonConverter(typeof(FiniteFloatConverter))]
    public float Volume { get; internal set; }

    /// <summary>Playback position in consumed PCM bytes, not milliseconds.</summary>
    /// <remarks>Null until the server reports it.</remarks>
    [JsonPropertyName("position"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? PositionBytes { get; internal set; }

    /// <summary>Unix timestamp of when the layer started, in milliseconds.</summary>
    /// <remarks>Null until the server reports it.</remarks>
    [JsonPropertyName("startTime"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? StartTimeMilliseconds { get; internal set; }

    /// <summary>
    /// When the layer started, null when unknown or out of range.
    /// </summary>
    [JsonIgnore]
    public DateTimeOffset? StartTime =>
        StartTimeMilliseconds is > 0 and <= UnixTime.MaxMilliseconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(StartTimeMilliseconds.Value)
            : null;

    /// <summary>Indicates whether the layers carry the same members.</summary>
    /// <param name="other">The layer to compare with.</param>
    /// <returns>Whether the members match, the materialized track included.</returns>
    public bool Equals(MixLayer? other) =>
        other is not null
        && Id == other.Id
        && Track.Equals(other.Track)
        && Volume == other.Volume
        && PositionBytes == other.PositionBytes
        && StartTimeMilliseconds == other.StartTimeMilliseconds;

    /// <summary>Returns a hash of the layer's comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Id, Track, Volume, PositionBytes, StartTimeMilliseconds);

    /// <summary>Returns the layer with its normalized members.</summary>
    /// <returns>The layer's public members, without the raw fields.</returns>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"MixLayer {{ Id = {Id}, Track = {Track}, Volume = {Volume}, PositionBytes = {PositionBytes}, StartTime = {StartTime?.ToString(CultureInfo.InvariantCulture) ?? "null"} }}"
        );
}

internal sealed record MixLayerCreate(
    [property: JsonPropertyName("track")] PlayerUpdateTrack Track,
    [property: JsonPropertyName("volume")] float? Volume = null
);

internal sealed record MixLayerVolume([property: JsonPropertyName("volume")] float Volume);

internal sealed record MixLayerCollection(
    [property: JsonPropertyName("mixes")] IReadOnlyList<MixLayer> Mixes
);
