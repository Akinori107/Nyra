using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>The direct stream URL of a track, as NodeLink's track stream route resolved it.</summary>
/// <remarks>The server picks a default format when no itag was requested.</remarks>
public sealed record TrackStreamUrl
{
    /// <summary>The direct playback URL for the track.</summary>
    /// <remarks>Short-lived, re-resolve instead of caching it.</remarks>
    [JsonPropertyName("url"), JsonInclude]
    public string? Url { get; internal set; }

    /// <summary>The streaming protocol, e.g. <c>http</c>, <c>sabr</c> or <c>hls</c>.</summary>
    [JsonPropertyName("protocol"), JsonInclude]
    public string? Protocol { get; internal set; }

    /// <summary>The media container format, e.g. <c>m4a</c> or <c>webm/opus</c>.</summary>
    [JsonPropertyName("format"), JsonInclude]
    public string? Format { get; internal set; }

    /// <summary>
    /// The HLS manifest URL NodeLink attached alongside a direct URL, the protocol is then
    /// <c>http</c>.
    /// </summary>
    /// <remarks>
    /// When the protocol is <c>hls</c> the manifest itself travels in <see cref="Url"/> and this member is
    /// null. Short-lived like <see cref="Url"/>, re-resolve instead of caching it.
    /// </remarks>
    [JsonPropertyName("hlsUrl"), JsonInclude]
    public string? HlsUrl { get; internal set; }

    /// <summary>
    /// The itag actually resolved, echoed by the YouTube direct path, null when the answer is SABR, HLS, a
    /// mirrored source or a non-YouTube source (which carry no itag at all).
    /// </summary>
    [JsonPropertyName("itag"), JsonInclude]
    public int? Itag { get; internal set; }
}
