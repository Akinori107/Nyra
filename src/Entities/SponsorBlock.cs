using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>A SponsorBlock segment of the current track.</summary>
/// <remarks>
/// The server only refreshes segments on YouTube and YouTube Music tracks, so any other track can still report
/// the previous ones.
/// </remarks>
public sealed record SponsorBlockSegment
{
    /// <summary>The id of the segment.</summary>
    [JsonPropertyName("uuid"), JsonInclude]
    public string Uuid
    {
        get;
        init => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>Segment start, in milliseconds.</summary>
    /// <remarks>Null when the server did not map one.</remarks>
    [JsonPropertyName("start"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? Start { get; init; }

    /// <summary>Segment end, in milliseconds.</summary>
    /// <remarks>Null when the server did not map one.</remarks>
    [JsonPropertyName("end"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? End { get; init; }

    /// <summary>SponsorBlock category, e.g. <c>sponsor</c> or <c>selfpromo</c>.</summary>
    [JsonPropertyName("category"), JsonInclude]
    public string Category
    {
        get;
        init => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>SponsorBlock action type of the segment, e.g. <c>skip</c> or <c>mute</c>.</summary>
    /// <remarks>
    /// The node only ever skips matched segments and never mutes them. Absorbs a null the server sends,
    /// readers see the empty string.
    /// </remarks>
    [JsonPropertyName("actionType"), JsonInclude]
    public string ActionType
    {
        get;
        init => field = value ?? string.Empty;
    } = string.Empty;

    /// <summary>The votes the segment has in the SponsorBlock database.</summary>
    /// <remarks>
    /// A null vote count (a co-client that writes one) reads as zero, it is an informative counter, a failed
    /// read would cost the whole state or event.
    /// </remarks>
    [JsonPropertyName("votes"), JsonInclude]
    [JsonConverter(typeof(TolerantRoundedInt32Converter))]
    public int Votes { get; init; }

    /// <summary>Whether the segment is locked in the SponsorBlock database.</summary>
    [JsonPropertyName("locked"), JsonInclude]
    [JsonConverter(typeof(TolerantBooleanConverter))]
    public bool Locked { get; init; }

    /// <summary>Length of the video the segment was submitted for, in milliseconds.</summary>
    /// <remarks>
    /// A null duration (a co-client that writes one) reads as zero, it is an informative counter, a failed read
    /// would cost the whole state or event.
    /// </remarks>
    [JsonPropertyName("videoDuration"), JsonInclude]
    [JsonConverter(typeof(TolerantRoundedInt64Converter))]
    public long VideoDuration { get; init; }

    /// <summary>The description of the segment.</summary>
    [JsonPropertyName("description"), JsonInclude]
    public string Description
    {
        get;
        init => field = value ?? string.Empty;
    } = string.Empty;
}

/// <summary>SponsorBlock state of a player, as reported by the server.</summary>
public sealed record SponsorBlockState
{
    /// <summary>Whether the server skips segments.</summary>
    [JsonPropertyName("enabled"), JsonInclude]
    [JsonConverter(typeof(TolerantBooleanConverter))]
    public bool Enabled { get; internal set; }

    /// <summary>The categories the server skips, a null list reads as empty.</summary>
    [JsonPropertyName("categories"), JsonInclude]
    public IReadOnlyList<string> Categories
    {
        get;
        internal set => field = value is null ? [] : [.. value.Where(v => v is not null)];
    } = [];

    /// <summary>
    /// The action types the server fetches segments for, a matched segment is always skipped by seeking, a
    /// null list reads as empty.
    /// </summary>
    [JsonPropertyName("actionTypes"), JsonInclude]
    public IReadOnlyList<string> ActionTypes
    {
        get;
        internal set => field = value is null ? [] : [.. value.Where(v => v is not null)];
    } = [];

    /// <summary>The segments loaded for the current track, a null list reads as empty.</summary>
    [JsonPropertyName("segments"), JsonInclude]
    public IReadOnlyList<SponsorBlockSegment> Segments
    {
        get;
        internal set => field = value is null ? [] : [.. value.Where(v => v is not null)];
    } = [];

    /// <summary>The id of the segment that was skipped last.</summary>
    [JsonPropertyName("lastSkippedUuid"), JsonInclude]
    public string? LastSkippedUuid { get; internal set; }

    /// <summary>How long before a segment boundary the server skips, in milliseconds.</summary>
    [JsonPropertyName("skipMarginMs"), JsonInclude]
    [JsonConverter(typeof(TolerantRoundedInt32Converter))]
    public int SkipMarginMilliseconds { get; internal set; }

    /// <summary>Indicates whether the states carry the same members.</summary>
    /// <param name="other">The state to compare with.</param>
    /// <returns>Whether the members match, the lists compared by content.</returns>
    public bool Equals(SponsorBlockState? other) =>
        other is not null
        && Enabled == other.Enabled
        && ContentEquality.ListEquals(Categories, other.Categories)
        && ContentEquality.ListEquals(ActionTypes, other.ActionTypes)
        && ContentEquality.ListEquals(Segments, other.Segments)
        && LastSkippedUuid == other.LastSkippedUuid
        && SkipMarginMilliseconds == other.SkipMarginMilliseconds;

    /// <summary>Returns a hash of the state's comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Enabled, LastSkippedUuid, SkipMarginMilliseconds);
}

/// <summary>SponsorBlock settings to change, members left null keep their current value.</summary>
public sealed record SponsorBlockSettings
{
    /// <summary>Whether the server skips segments, null keeps the current setting.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>The categories to skip, null keeps the current setting.</summary>
    [JsonPropertyName("categories")]
    public IReadOnlyList<string>? Categories { get; init; }

    /// <summary>
    /// The action types to fetch segments for, a matched segment is always skipped by seeking, null keeps the
    /// current setting.
    /// </summary>
    [JsonPropertyName("actionTypes")]
    public IReadOnlyList<string>? ActionTypes { get; init; }

    /// <summary>The margin applied when skipping, in milliseconds, null keeps the current setting.</summary>
    [JsonPropertyName("skipMarginMs")]
    public int? SkipMarginMilliseconds { get; init; }

    /// <summary>Indicates whether the settings carry the same members.</summary>
    /// <param name="other">The settings to compare with.</param>
    /// <returns>Whether the members match, the lists compared by content.</returns>
    public bool Equals(SponsorBlockSettings? other) =>
        other is not null
        && Enabled == other.Enabled
        && ContentEquality.ListEquals(Categories, other.Categories)
        && ContentEquality.ListEquals(ActionTypes, other.ActionTypes)
        && SkipMarginMilliseconds == other.SkipMarginMilliseconds;

    /// <summary>Returns a hash of the settings' comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Enabled, SkipMarginMilliseconds);
}

internal sealed record SponsorBlockSegmentOverride(
    [property: JsonPropertyName("segments")] IReadOnlyList<SponsorBlockSegment> Segments
);
