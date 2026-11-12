using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>The route planner implementation the server reports.</summary>
/// <remarks>NodeLink always says balancing, whatever its configured strategy is.</remarks>
public enum RoutePlannerClass
{
    /// <summary>A planner class the library does not know, reported by a newer server.</summary>
    /// <remarks>Also used when the response has none.</remarks>
    Unknown,

    /// <summary>One address of the block per retry.</summary>
    Rotating,

    /// <summary>A random address of the block per request.</summary>
    Nano,

    /// <summary>
    /// A random address of the current /64 sub-block per request, the sub-block rotates per failure.
    /// </summary>
    RotatingNano,

    /// <summary>A random address of the block per request.</summary>
    Balancing,
}

/// <summary>Address family of the planner's IP block.</summary>
public enum IpBlockType
{
    /// <summary>An address family the library does not know, reported by a newer server.</summary>
    /// <remarks>Also used when the response has none.</remarks>
    Unknown,

    /// <summary>The block holds IPv4 addresses.</summary>
    Inet4,

    /// <summary>The block holds IPv6 addresses.</summary>
    Inet6,
}

/// <summary>An address the planner currently treats as failing.</summary>
public sealed record FailingAddress
{
    /// <summary>The failing address, in the server's own format.</summary>
    /// <remarks>
    /// Lavalink sends it with a leading slash ("/1.0.0.0"), NodeLink sends the plain address. Nyra keeps it as the
    /// server sent it. Never null, a missing value reads as an empty string.
    /// </remarks>
    [JsonPropertyName("failingAddress"), JsonInclude]
    public string Address
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Unix timestamp, in milliseconds, since when the address fails.</summary>
    [JsonPropertyName("failingTimestamp"), JsonInclude]
    public long FailingTimestampMilliseconds { get; internal set; }

    /// <summary>Since when the address fails, as a date.</summary>
    /// <remarks>
    /// <see cref="DateTimeOffset.MinValue"/> when the value is out of range.
    /// </remarks>
    [JsonIgnore]
    public DateTimeOffset FailingTimestamp =>
        UnixTime.ToDateTimeOffset(FailingTimestampMilliseconds);

    /// <summary>The same timestamp as the server formatted it.</summary>
    [JsonPropertyName("failingTime"), JsonInclude]
    public string FailingTime
    {
        get;
        internal set => field = value ?? string.Empty;
    } = string.Empty;
}

/// <summary>The IP pool the planner draws addresses from.</summary>
public sealed record RoutePlannerIpBlock
{
    /// <summary>The kind of addresses the block holds.</summary>
    [JsonPropertyName("type"), JsonInclude]
    public IpBlockType Type { get; internal set; }

    /// <summary>Block size as reported, the servers measure different things, so it is not comparable.</summary>
    [JsonPropertyName("size"), JsonInclude]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string? Size { get; internal set; }
}

/// <summary>Planner details.</summary>
/// <remarks>The strategy-specific members are present only for their strategies.</remarks>
public sealed record RoutePlannerDetails
{
    /// <summary>The block the planner draws from, null when the server sent none.</summary>
    [JsonPropertyName("ipBlock"), JsonInclude]
    public RoutePlannerIpBlock? IpBlock { get; internal set; }

    /// <summary>The addresses that failed, with the failure and when.</summary>
    [JsonPropertyName("failingAddresses"), JsonInclude]
    public IReadOnlyList<FailingAddress>? FailingAddresses
    {
        get;
        internal set => field = value is null ? null : [.. value.Where(v => v is not null)];
    }

    /// <summary>Indicates whether both details describe the same planner state.</summary>
    /// <param name="other">The details to compare with.</param>
    /// <returns>Whether the members match, the addresses compared by content.</returns>
    public bool Equals(RoutePlannerDetails? other) =>
        other is not null
        && IpBlock == other.IpBlock
        && Strategy == other.Strategy
        && RotateIndex == other.RotateIndex
        && IpIndex == other.IpIndex
        && CurrentAddress == other.CurrentAddress
        && CurrentAddressIndex == other.CurrentAddressIndex
        && BlockIndex == other.BlockIndex
        && ContentEquality.ListEquals(FailingAddresses, other.FailingAddresses);

    /// <summary>Returns a hash of the scalar members, the addresses stay out of it.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(
            IpBlock,
            Strategy,
            RotateIndex,
            IpIndex,
            CurrentAddress,
            CurrentAddressIndex,
            BlockIndex
        );

    /// <summary>
    /// NodeLink reports the configured strategy (RotateOnBan, RoundRobin, LoadBalance) under this member,
    /// Lavalink does not send it.
    /// </summary>
    [JsonPropertyName("strategy"), JsonInclude]
    public string? Strategy { get; internal set; }

    /// <summary>Rotating planner, the number of rotations performed so far.</summary>
    [JsonPropertyName("rotateIndex"), JsonInclude]
    public string? RotateIndex { get; internal set; }

    /// <summary>Rotating planner, current index inside the block.</summary>
    [JsonPropertyName("ipIndex"), JsonInclude]
    public string? IpIndex { get; internal set; }

    /// <summary>
    /// Rotating planner, the address in use, with a leading slash ("/1.0.0.0").
    /// </summary>
    /// <remarks>NodeLink always sends null here.</remarks>
    [JsonPropertyName("currentAddress"), JsonInclude]
    public string? CurrentAddress { get; internal set; }

    /// <summary>
    /// Nano planner, the offset the current address was drawn at, rotating-nano, the same offset within the
    /// current /64 block.
    /// </summary>
    /// <remarks>These planners change address all the time.</remarks>
    [JsonPropertyName("currentAddressIndex"), JsonInclude]
    public string? CurrentAddressIndex { get; internal set; }

    /// <summary>Rotating-nano planner, current block index.</summary>
    [JsonPropertyName("blockIndex"), JsonInclude]
    public string? BlockIndex { get; internal set; }
}

/// <summary>Status of the server's IP route planner, served by both.</summary>
/// <remarks>
/// With no planner configured, Lavalink makes the call throw and NodeLink answers a status with a size of 0.
/// </remarks>
public sealed record RoutePlannerStatus
{
    /// <summary>The planner the server uses.</summary>
    [JsonPropertyName("class"), JsonInclude]
    public RoutePlannerClass Class { get; internal set; }

    /// <summary>The details of the planner, null when the server sent none.</summary>
    [JsonPropertyName("details"), JsonInclude]
    public RoutePlannerDetails? Details { get; internal set; }
}

internal sealed record RoutePlannerFreeAddress(
    [property: JsonPropertyName("address")] string Address
);
