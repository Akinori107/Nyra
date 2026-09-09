using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Spotify cross-reference of an EternalBox track. NodeLink only.</summary>
public sealed record EternalBoxSpotify
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

/// <summary>Alternative ways to reach the audio of an EternalBox track. NodeLink only.</summary>
public sealed record EternalBoxLinks
{
    /// <summary>The page the EternalBox identifier resolves to.</summary>
    [JsonPropertyName("jukeboxPage")]
    public string? JukeboxPage { get; init; }

    [JsonPropertyName("analysisUrl")]
    public string? AnalysisUrl { get; init; }

    [JsonPropertyName("streamUrl")]
    public string? StreamUrl { get; init; }

    /// <summary>The source audio the EternalBox analysis was built from.</summary>
    [JsonPropertyName("ogAudioSource")]
    public string? OgAudioSource { get; init; }

    [JsonPropertyName("spotifyUrl")]
    public string? SpotifyUrl { get; init; }
}

/// <summary>Context of the EternalBox continuous-playback engine for the playing track. NodeLink only.</summary>
public sealed record EternalBoxInfo
{
    /// <summary>EternalBox identifier of the track.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Service the track was sourced from, such as spotify.</summary>
    [JsonPropertyName("service")]
    public string? Service { get; init; }

    /// <summary>Summarized beat analysis of the track, base64-encoded.</summary>
    [JsonPropertyName("analysisSummary")]
    public string? AnalysisSummary { get; init; }

    [JsonPropertyName("spotify")]
    public EternalBoxSpotify? Spotify { get; init; }

    [JsonPropertyName("links")]
    public EternalBoxLinks? Links { get; init; }
}

/// <summary>Why the EternalBox engine moved to another beat. NodeLink only.</summary>
public enum EternalBoxJumpType
{
    /// <summary>The beat queue wrapped around to the beginning.</summary>
    [JsonPropertyName("loop")]
    Loop,

    /// <summary>The engine branched to a neighboring beat.</summary>
    [JsonPropertyName("jump")]
    Jump,

    /// <summary>A jump type a newer server reported.</summary>
    [JsonPropertyName("unknown")]
    Unknown,
}

/// <summary>A beat-to-beat move of the EternalBox engine. NodeLink only.</summary>
public sealed record EternalBoxJump
{
    /// <summary>EternalBox identifier of the track.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Beat index the playback moved from.</summary>
    [JsonPropertyName("fromBeat")]
    public int FromBeat { get; init; }

    /// <summary>Beat index the playback moved to.</summary>
    [JsonPropertyName("toBeat")]
    public int ToBeat { get; init; }

    [JsonPropertyName("type")]
    public EternalBoxJumpType Type { get; init; }
}
