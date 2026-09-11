using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Classification of the node's own outbound network health. NodeLink only.</summary>
public enum ConnectionQuality
{
    /// <summary>No connectivity check has completed yet.</summary>
    [JsonPropertyName("unknown")]
    Unknown,

    /// <summary>The last speed test was above both thresholds, the connection is healthy.</summary>
    [JsonPropertyName("good")]
    Good,

    /// <summary>The last speed test was below the good threshold but above the bad one.</summary>
    [JsonPropertyName("average")]
    Average,

    /// <summary>The last speed test was below the bad threshold, the connection is very slow.</summary>
    [JsonPropertyName("bad")]
    Bad,

    /// <summary>Every speed test attempted so far has failed.</summary>
    [JsonPropertyName("disconnected")]
    Disconnected,

    /// <summary>The connection manager is unavailable in the process that served the REST request.</summary>
    [JsonPropertyName("disabled")]
    Disabled,
}

/// <summary>
/// The node's own outbound network health, as reported by <c>GET /v4/connection</c> or pushed
/// through <c>ConnectionStatusEvent</c>. Unrelated to any specific guild's voice connection.
/// NodeLink only.
/// </summary>
public sealed record ConnectionStatus
{
    [JsonPropertyName("status")]
    public ConnectionQuality Status { get; init; }

    /// <summary>Raw speed-test, DNS and ping metrics; shape varies with which checks ran. Null when <see cref="Status"/> is <see cref="ConnectionQuality.Disabled"/>.</summary>
    [JsonPropertyName("metrics")]
    public JsonElement? Metrics { get; init; }

    /// <summary>Machine-readable reason for the disabled state. Only present when <see cref="Status"/> is <see cref="ConnectionQuality.Disabled"/>.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
