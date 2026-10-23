using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Classification of the node's own outbound network health.</summary>
/// <remarks>NodeLink only.</remarks>
public enum ConnectionQuality
{
    /// <summary>No connectivity check has completed yet.</summary>
    Unknown,

    /// <summary>The last speed test was above both thresholds, the connection is healthy.</summary>
    Good,

    /// <summary>The last speed test was below the good threshold but above the bad one.</summary>
    Average,

    /// <summary>The last speed test was below the bad threshold, the connection is very slow.</summary>
    Bad,

    /// <summary>Every speed test attempted so far has failed.</summary>
    Disconnected,

    /// <summary>The connection manager is unavailable in the process that served the REST request.</summary>
    Disabled,
}

/// <summary>
/// The node's own outbound network health, from <c>GET /v4/connection</c> or pushed through
/// <c>ConnectionStatusEvent</c>.
/// </summary>
/// <remarks>Unrelated to any guild's voice connection. NodeLink only.</remarks>
public sealed record ConnectionStatus
{
    /// <summary>The connection quality the node reports.</summary>
    /// <remarks>
    /// A null token reads as <see cref="ConnectionQuality.Unknown"/>, the same degradation an absent member
    /// and an unknown string already get, so a server that pushes a null status mid-degradation keeps its
    /// frame alive instead of losing it whole.
    /// </remarks>
    [JsonPropertyName("status"), JsonInclude]
    public ConnectionQuality Status { get; internal set; }

    /// <summary>
    /// Raw speed-test, DNS, ping and local network-interface metrics, shape varies with which checks ran.
    /// </summary>
    /// <remarks>Null when <see cref="Status"/> is <see cref="ConnectionQuality.Disabled"/>.</remarks>
    [JsonPropertyName("metrics"), JsonInclude]
    public JsonElement? Metrics { get; internal set; }

    /// <summary>Machine-readable reason for the disabled state.</summary>
    /// <remarks>
    /// Only present when <see cref="Status"/> is <see cref="ConnectionQuality.Disabled"/>. A token that is
    /// not a string reads as null instead of failing the whole frame.
    /// </remarks>
    [JsonPropertyName("reason"), JsonInclude]
    [JsonConverter(typeof(NullableTolerantStringConverter))]
    public string? Reason { get; internal set; }

    /// <summary>Indicates whether the statuses report the same quality and reason.</summary>
    /// <param name="other">The status to compare with.</param>
    /// <returns>Whether the statuses are equal, metrics compared by content.</returns>
    public bool Equals(ConnectionStatus? other) =>
        other is not null
        && Status == other.Status
        && Reason == other.Reason
        && ContentEquality.JsonElementEquals(Metrics, other.Metrics);

    /// <summary>Returns the hash code of the status.</summary>
    /// <returns>A hash of the status and reason, the metrics stay out of it.</returns>
    public override int GetHashCode() => HashCode.Combine(Status, Reason);
}
