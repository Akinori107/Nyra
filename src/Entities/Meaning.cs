using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Outcome of a meaning load.</summary>
public enum MeaningLoadType
{
    /// <summary>A meaning was found.</summary>
    [JsonPropertyName("meaning")]
    Meaning,

    /// <summary>No provider had a meaning for the track.</summary>
    [JsonPropertyName("empty")]
    Empty,

    /// <summary>Every provider failed.</summary>
    [JsonPropertyName("error")]
    Error,
}

/// <summary>Language pair of a meaning translation.</summary>
public sealed record MeaningTranslationLanguage
{
    /// <summary>Language the text was translated from.</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    /// <summary>Language the text was translated to.</summary>
    [JsonPropertyName("target")]
    public string? Target { get; init; }
}

/// <summary>Translated counterpart of a meaning text.</summary>
public sealed record MeaningTranslation
{
    [JsonPropertyName("language")]
    public MeaningTranslationLanguage? Language { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("paragraphs")]
    public IReadOnlyList<string> Paragraphs { get; init; } = [];
}

/// <summary>Editorial metadata of a meaning, as Letras.mus curates it.</summary>
public sealed record MeaningMeta
{
    /// <summary>Letras embeds this as a raw JSON value, so it arrives as a string or a number, this reads either.</summary>
    [JsonPropertyName("id")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string? Id { get; init; }

    /// <summary>Letras embeds this as a raw JSON value, so it arrives as a string or a number, this reads either.</summary>
    [JsonPropertyName("localeId")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string? LocaleId { get; init; }

    /// <summary>Letras embeds this as a raw JSON value, so it arrives as a string or a number, this reads either.</summary>
    [JsonPropertyName("origin")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string? Origin { get; init; }

    [JsonPropertyName("submittedBy")]
    public string? SubmittedBy { get; init; }

    [JsonPropertyName("reviewedBy")]
    public string? ReviewedBy { get; init; }
}

/// <summary>The song the meaning was written about.</summary>
public sealed record MeaningSong
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("artist")]
    public string? Artist { get; init; }

    [JsonPropertyName("youtubeId")]
    public string? YoutubeId { get; init; }

    /// <summary>Letras embeds this as a raw JSON value, so it arrives as a string or a number, this reads either.</summary>
    [JsonPropertyName("letrasId")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string? LetrasId { get; init; }

    [JsonPropertyName("artworkUrl")]
    public string? ArtworkUrl { get; init; }
}

/// <summary>The meaning text of a track: a summary of what the song is about.</summary>
public sealed record MeaningData
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("paragraphs")]
    public IReadOnlyList<string> Paragraphs { get; init; } = [];

    /// <summary>Page the meaning was taken from.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>Machine translation of the meaning, requested through the language parameter.</summary>
    [JsonPropertyName("translation")]
    public MeaningTranslation? Translation { get; init; }

    [JsonPropertyName("meaningMeta")]
    public MeaningMeta? Meta { get; init; }

    [JsonPropertyName("song")]
    public MeaningSong? Song { get; init; }

    /// <summary>What the provider took the meaning for, such as track.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Provider that produced the meaning, such as wikipedia or letrasmus.</summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    /// <summary>Present only when <see cref="MeaningResult.LoadType"/> is <see cref="MeaningLoadType.Error"/>.</summary>
    [JsonPropertyName("message")]
    public string? ErrorMessage { get; init; }

    /// <summary>Present only when <see cref="MeaningResult.LoadType"/> is <see cref="MeaningLoadType.Error"/>.</summary>
    [JsonPropertyName("severity")]
    public ErrorSeverity? ErrorSeverity { get; init; }
}

/// <summary>Outcome of a meaning load, the data member is present only for a successful one.</summary>
public sealed record MeaningResult
{
    [JsonPropertyName("loadType")]
    public MeaningLoadType LoadType { get; init; }

    [JsonPropertyName("data")]
    public MeaningData? Data { get; init; }
}
