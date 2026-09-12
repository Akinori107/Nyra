using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>The rotation strategy the server's IP block planner uses.</summary>
public enum RoutePlannerClass
{
    [JsonPropertyName("RotatingIpRoutePlanner")]
    Rotating,

    [JsonPropertyName("NanoIpRoutePlanner")]
    Nano,

    [JsonPropertyName("RotatingNanoIpRoutePlanner")]
    RotatingNano,

    [JsonPropertyName("BalancingIpRoutePlanner")]
    Balancing,

    /// <summary>A strategy a newer server reported.</summary>
    [JsonPropertyName("unknown")]
    Unknown,
}

/// <summary>Address family of the planner's IP block.</summary>
public enum IpBlockType
{
    [JsonPropertyName("Inet4Address")]
    Inet4,

    [JsonPropertyName("Inet6Address")]
    Inet6,

    /// <summary>A family a newer server reported.</summary>
    [JsonPropertyName("unknown")]
    Unknown,
}

/// <summary>An address the planner currently treats as failing.</summary>
public sealed record FailingAddress
{
    [JsonPropertyName("failingAddress")]
    public string Address { get; init; } = string.Empty;

    /// <summary>Unix timestamp, in milliseconds, since when the address fails.</summary>
    [JsonPropertyName("failingTimestamp")]
    public long FailingTimestampMilliseconds { get; init; }

    /// <summary>The same timestamp as the server formatted it.</summary>
    [JsonPropertyName("failingTime")]
    public string FailingTime { get; init; } = string.Empty;
}

/// <summary>The IP pool the planner draws addresses from.</summary>
public sealed record RoutePlannerIpBlock
{
    [JsonPropertyName("type")]
    public IpBlockType Type { get; init; }

    /// <summary>
    /// Address count of the block. Lavalink formats it as a string (<c>ipBlock.size.toString()</c>),
    /// NodeLink sends the raw <c>.length</c> as a JSON number, so this reads either.
    /// </summary>
    [JsonPropertyName("size")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string? Size { get; init; }
}

/// <summary>Planner details. The strategy-specific members are present only for their strategies.</summary>
public sealed record RoutePlannerDetails
{
    [JsonPropertyName("ipBlock")]
    public RoutePlannerIpBlock? IpBlock { get; init; }

    [JsonPropertyName("failingAddresses")]
    public IReadOnlyList<FailingAddress>? FailingAddresses { get; init; }

    /// <summary>NodeLink reports the configured strategy (RotateOnBan, RoundRobin, LoadBalance) under this member, Lavalink does not send it.</summary>
    [JsonPropertyName("strategy")]
    public string? Strategy { get; init; }

    /// <summary>Rotating planner: how far the block was rotated through.</summary>
    [JsonPropertyName("rotateIndex")]
    public string? RotateIndex { get; init; }

    /// <summary>Rotating planner: current index inside the block.</summary>
    [JsonPropertyName("ipIndex")]
    public string? IpIndex { get; init; }

    /// <summary>Rotating planner: the address currently in use.</summary>
    [JsonPropertyName("currentAddress")]
    public string? CurrentAddress { get; init; }

    /// <summary>Nano and rotating-nano planners: current position in the address space.</summary>
    [JsonPropertyName("currentAddressIndex")]
    public string? CurrentAddressIndex { get; init; }

    /// <summary>Rotating-nano planner: current block index.</summary>
    [JsonPropertyName("blockIndex")]
    public string? BlockIndex { get; init; }
}

/// <summary>Status of the server's IP route planner. Served by both servers. The request fails when none is configured.</summary>
public sealed record RoutePlannerStatus
{
    [JsonPropertyName("class")]
    public RoutePlannerClass Class { get; init; }

    [JsonPropertyName("details")]
    public RoutePlannerDetails? Details { get; init; }
}

/// <summary>Body of the free-address request.</summary>
internal sealed record RoutePlannerFreeAddress(
    [property: JsonPropertyName("address")] string Address
);
