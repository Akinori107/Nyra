using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Spotify cross-reference of an EternalBox track.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed record EternalBoxSpotify
{
    /// <summary>
    /// Spotify track id, NodeLink falls back to the EternalBox identifier when Spotify reported none.
    /// </summary>
    /// <remarks>Null when the server sent none.</remarks>
    [JsonPropertyName("id"), JsonInclude]
    public string? Id { get; internal set; }

    /// <summary>
    /// Spotify page of the track, falls back to the original source page when Spotify reported no URL.
    /// </summary>
    /// <remarks>Null when the server sent none.</remarks>
    [JsonPropertyName("url"), JsonInclude]
    public string? Url { get; internal set; }

    /// <summary>The ISRC of the track on Spotify, null when the server sent none.</summary>
    [JsonPropertyName("isrc"), JsonInclude]
    public string? Isrc { get; internal set; }

    /// <summary>The artwork of the track on Spotify, null when the server sent none.</summary>
    [JsonPropertyName("artworkUrl"), JsonInclude]
    public string? ArtworkUrl { get; internal set; }

    /// <summary>Spotify's own duration of the track, null when Spotify did not report one.</summary>
    [JsonPropertyName("durationMs"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? DurationMs { get; internal set; }

    /// <summary>A preview clip of the track on Spotify, null when the server sent none.</summary>
    [JsonPropertyName("previewUrl"), JsonInclude]
    public string? PreviewUrl { get; internal set; }
}

/// <summary>
/// Count summary of the EternalBox beat analysis, every count is null when the analysis could not be built for
/// the track.
/// </summary>
public sealed record EternalBoxAnalysisSummary
{
    /// <summary>
    /// Duration the summary was computed against, the track's own length when the server knows it, otherwise
    /// the duration reported by the beat analysis.
    /// </summary>
    /// <remarks>Null when neither is known.</remarks>
    [JsonPropertyName("durationMs"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? DurationMs { get; internal set; }

    /// <summary>The number of beats the analysis found.</summary>
    [JsonPropertyName("beats"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? Beats { get; internal set; }

    /// <summary>The number of bars the analysis found.</summary>
    [JsonPropertyName("bars"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? Bars { get; internal set; }

    /// <summary>The number of sections the analysis found.</summary>
    [JsonPropertyName("sections"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? Sections { get; internal set; }

    /// <summary>The number of tatums the analysis found.</summary>
    [JsonPropertyName("tatums"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? Tatums { get; internal set; }

    /// <summary>The number of segments the analysis found.</summary>
    [JsonPropertyName("segments"), JsonInclude]
    [JsonConverter(typeof(RoundedInt64Converter))]
    public long? Segments { get; internal set; }
}

/// <summary>Alternate URLs for an EternalBox track, pages, analysis, and audio sources.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed record EternalBoxLinks
{
    /// <summary>
    /// The track's URI, the original source page when the payload carried one, otherwise the EternalBox
    /// jukebox page.
    /// </summary>
    /// <remarks>Null when the server sent none.</remarks>
    [JsonPropertyName("jukeboxPage"), JsonInclude]
    public string? JukeboxPage { get; internal set; }

    /// <summary>The URL of the full analysis, null when the server sent none.</summary>
    [JsonPropertyName("analysisUrl"), JsonInclude]
    public string? AnalysisUrl { get; internal set; }

    /// <summary>The URL of the audio stream, null when the server sent none.</summary>
    [JsonPropertyName("streamUrl"), JsonInclude]
    public string? StreamUrl { get; internal set; }

    /// <summary>The source audio the EternalBox analysis was built from, null when the server sent none.</summary>
    [JsonPropertyName("ogAudioSource"), JsonInclude]
    public string? OgAudioSource { get; internal set; }

    /// <summary>
    /// Spotify page of the track, falls back to the track URI when Spotify enrichment reported no URL.
    /// </summary>
    /// <remarks>Null when the server sent none.</remarks>
    [JsonPropertyName("spotifyUrl"), JsonInclude]
    public string? SpotifyUrl { get; internal set; }
}

/// <summary>Context of the EternalBox continuous-playback engine for the playing track.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed record EternalBoxInfo
{
    /// <summary>The id of the track on the provider, null when the server sent none.</summary>
    [JsonPropertyName("id"), JsonInclude]
    public string? Id { get; internal set; }

    /// <summary>Service the track was sourced from, e.g. <c>SPOTIFY</c>, null when the server sent none.</summary>
    [JsonPropertyName("service"), JsonInclude]
    public string? Service { get; internal set; }

    /// <summary>Count summary of the track's beat analysis, null when the server sent none.</summary>
    [JsonPropertyName("analysisSummary"), JsonInclude]
    public EternalBoxAnalysisSummary? AnalysisSummary { get; internal set; }

    /// <summary>The Spotify details of the track.</summary>
    [JsonPropertyName("spotify"), JsonInclude]
    public EternalBoxSpotify? Spotify { get; internal set; }

    /// <summary>The links to the pages, analysis, and audio sources of the box.</summary>
    [JsonPropertyName("links"), JsonInclude]
    public EternalBoxLinks? Links { get; internal set; }
}

/// <summary>Why the EternalBox engine moved to another beat.</summary>
/// <remarks>NodeLink only.</remarks>
public enum EternalBoxJumpType
{
    /// <summary>A jump type the library does not know, reported by a newer server.</summary>
    /// <remarks>
    /// The fallback for wire values and non-string tokens the library does not know, a frame without
    /// a type reads as null, see EternalBoxJump.Type.
    /// </remarks>
    Unknown,

    /// <summary>The beat queue wrapped around to the beginning.</summary>
    Loop,

    /// <summary>The engine branched to a neighboring beat.</summary>
    Jump,
}

/// <summary>A beat-to-beat move of the EternalBox engine.</summary>
/// <remarks>NodeLink only.</remarks>
public sealed record EternalBoxJump
{
    /// <summary>The id of the EternalBox track the jump moved within, null when the server sent none.</summary>
    [JsonPropertyName("id"), JsonInclude]
    public string? Id { get; internal set; }

    /// <summary>The beat the jump starts from, null when the server sent none.</summary>
    [JsonPropertyName("fromBeat"), JsonInclude]
    public int? FromBeat { get; internal set; }

    /// <summary>The beat the jump lands on, null when the server sent none.</summary>
    [JsonPropertyName("toBeat"), JsonInclude]
    public int? ToBeat { get; internal set; }

    /// <summary>What the jump does, null when the server sent none.</summary>
    [JsonPropertyName("type"), JsonInclude]
    public EternalBoxJumpType? Type { get; internal set; }
}
