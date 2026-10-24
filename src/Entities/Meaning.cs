using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>Outcome of a meaning load.</summary>
public enum MeaningLoadType
{
    /// <summary>An outcome the library does not know, reported by a newer server.</summary>
    /// <remarks>
    /// It is the default, a response without a load type reads as Unknown instead of fabricating a known one.
    /// </remarks>
    Unknown,

    /// <summary>A provider returned a meaning for the track.</summary>
    Meaning,

    /// <summary>No provider returned a meaning for the track.</summary>
    Empty,

    /// <summary>
    /// The meaning load failed, a provider errored or the request was invalid, other providers may not have
    /// been tried.
    /// </summary>
    Error,
}

/// <summary>Language pair of a meaning translation.</summary>
public sealed record MeaningTranslationLanguage
{
    /// <summary>The language the text is translated from.</summary>
    [JsonPropertyName("source"), JsonInclude]
    public string? Source { get; internal set; }

    /// <summary>The language the text is translated to.</summary>
    [JsonPropertyName("target"), JsonInclude]
    public string? Target { get; internal set; }
}

/// <summary>Translated counterpart of a meaning text.</summary>
public sealed record MeaningTranslation
{
    /// <summary>The languages of the translation.</summary>
    [JsonPropertyName("language"), JsonInclude]
    public MeaningTranslationLanguage? Language { get; internal set; }

    /// <summary>The translated title.</summary>
    [JsonPropertyName("title"), JsonInclude]
    public string? Title { get; internal set; }

    /// <summary>The translated description.</summary>
    [JsonPropertyName("description"), JsonInclude]
    public string? Description { get; internal set; }

    /// <summary>The translated paragraphs.</summary>
    [JsonPropertyName("paragraphs"), JsonInclude]
    public IReadOnlyList<string> Paragraphs
    {
        get;
        internal set => field = value is null ? [] : [.. value.Where(v => v is not null)];
    } = [];

    /// <summary>Returns the translation as its title and paragraph count.</summary>
    /// <returns>The translation's title and paragraph count.</returns>
    public override string ToString() => $"{Title ?? string.Empty} ({Paragraphs.Count} paragraphs)";

    /// <summary>Indicates whether both translations carry the same text and paragraphs.</summary>
    /// <param name="other">The translation to compare with.</param>
    /// <returns>Whether every member matches, the paragraphs compared by content.</returns>
    public bool Equals(MeaningTranslation? other) =>
        other is not null
        && Language == other.Language
        && Title == other.Title
        && Description == other.Description
        && ContentEquality.ListEquals(Paragraphs, other.Paragraphs);

    /// <summary>Returns a hash of the translation's comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Language, Title, Description);
}

/// <summary>Editorial metadata of a meaning, as Letras.mus curates it.</summary>
/// <remarks>
/// The id-like members of this record arrive as a raw JSON value that Letras writes as a string or a number,
/// this reads either.
/// </remarks>
public sealed record MeaningMeta
{
    /// <summary>The id of the meaning on Letras.</summary>
    [JsonPropertyName("id"), JsonInclude]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string? Id { get; internal set; }

    /// <summary>The locale of the meaning, as Letras reports it.</summary>
    [JsonPropertyName("localeId"), JsonInclude]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string? LocaleId { get; internal set; }

    /// <summary>The origin of the meaning, as Letras reports it.</summary>
    [JsonPropertyName("origin"), JsonInclude]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string? Origin { get; internal set; }

    /// <summary>Who submitted the meaning.</summary>
    [JsonPropertyName("submittedBy"), JsonInclude]
    public string? SubmittedBy { get; internal set; }

    /// <summary>Who reviewed the meaning.</summary>
    [JsonPropertyName("reviewedBy"), JsonInclude]
    public string? ReviewedBy { get; internal set; }
}

/// <summary>The song the meaning was written about.</summary>
public sealed record MeaningSong
{
    /// <summary>The title of the song.</summary>
    [JsonPropertyName("title"), JsonInclude]
    public string? Title { get; internal set; }

    /// <summary>The artist of the song.</summary>
    [JsonPropertyName("artist"), JsonInclude]
    public string? Artist { get; internal set; }

    /// <summary>The YouTube id of the song.</summary>
    [JsonPropertyName("youtubeId"), JsonInclude]
    public string? YoutubeId { get; internal set; }

    /// <summary>The Letras id of the song, arrives as a string or a number.</summary>
    [JsonPropertyName("letrasId"), JsonInclude]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string? LetrasId { get; internal set; }

    /// <summary>The artwork of the song.</summary>
    [JsonPropertyName("artworkUrl"), JsonInclude]
    public string? ArtworkUrl { get; internal set; }
}

/// <summary>The meaning text of a track, a summary of what the song is about.</summary>
public sealed record MeaningData
{
    /// <summary>The title of the meaning.</summary>
    [JsonPropertyName("title"), JsonInclude]
    public string? Title { get; internal set; }

    /// <summary>The description of the meaning.</summary>
    [JsonPropertyName("description"), JsonInclude]
    public string? Description { get; internal set; }

    /// <summary>The paragraphs of the meaning.</summary>
    [JsonPropertyName("paragraphs"), JsonInclude]
    public IReadOnlyList<string> Paragraphs
    {
        get;
        internal set => field = value is null ? [] : [.. value.Where(v => v is not null)];
    } = [];

    /// <summary>The URL the meaning was loaded from.</summary>
    [JsonPropertyName("url"), JsonInclude]
    public string? Url { get; internal set; }

    /// <summary>Returns the meaning as its title and paragraph count.</summary>
    /// <returns>The meaning's title and paragraph count.</returns>
    public override string ToString() => $"{Title ?? string.Empty} ({Paragraphs.Count} paragraphs)";

    /// <summary>Indicates whether both meanings carry the same members.</summary>
    /// <param name="other">The meaning to compare with.</param>
    /// <returns>Whether every member matches, the paragraphs compared by content.</returns>
    public bool Equals(MeaningData? other) =>
        other is not null
        && Title == other.Title
        && Description == other.Description
        && ContentEquality.ListEquals(Paragraphs, other.Paragraphs)
        && Url == other.Url
        && Translation == other.Translation
        && Meta == other.Meta
        && Song == other.Song
        && Type == other.Type
        && Provider == other.Provider
        && ErrorMessage == other.ErrorMessage
        && ErrorSeverity == other.ErrorSeverity;

    /// <summary>Returns a hash of the meaning's comparable members.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(Title, Description, Url, Type, Provider);

    /// <summary>Machine translation of the meaning, requested through the language parameter.</summary>
    [JsonPropertyName("translation"), JsonInclude]
    public MeaningTranslation? Translation { get; internal set; }

    /// <summary>The metadata of the meaning.</summary>
    [JsonPropertyName("meaningMeta"), JsonInclude]
    public MeaningMeta? Meta { get; internal set; }

    /// <summary>The song the meaning is about.</summary>
    [JsonPropertyName("song"), JsonInclude]
    public MeaningSong? Song { get; internal set; }

    /// <summary>What the provider took the meaning for, track or artist.</summary>
    [JsonPropertyName("type"), JsonInclude]
    public string? Type { get; internal set; }

    /// <summary>Provider that produced the meaning, such as wikipedia or letrasmus.</summary>
    [JsonPropertyName("provider"), JsonInclude]
    public string? Provider { get; internal set; }

    /// <summary>
    /// Present only when <see cref="MeaningResult.LoadType"/> is <see cref="MeaningLoadType.Error"/>.
    /// </summary>
    [JsonPropertyName("message"), JsonInclude]
    public string? ErrorMessage { get; internal set; }

    /// <summary>
    /// Present only when <see cref="MeaningResult.LoadType"/> is <see cref="MeaningLoadType.Error"/>.
    /// </summary>
    /// <remarks>
    /// Null when the server reports the error as a bare message, set when it reports the severity in-band.
    /// </remarks>
    [JsonPropertyName("severity"), JsonInclude]
    public ErrorSeverity? ErrorSeverity { get; internal set; }
}

/// <summary>
/// Outcome of a meaning load, the data member is present for every load type, the meaning when Meaning, an
/// empty object when Empty, the error details when Error.
/// </summary>
public sealed record MeaningResult
{
    /// <summary>The outcome of the meaning load.</summary>
    [JsonPropertyName("loadType"), JsonInclude]
    public MeaningLoadType LoadType { get; internal set; }

    /// <summary>The meaning on Meaning, an empty object on Empty, the error details on Error.</summary>
    /// <remarks>
    /// On <see cref="MeaningLoadType.Unknown"/> it holds whatever the server put in the envelope's data
    /// slot, if anything.
    /// </remarks>
    [JsonPropertyName("data"), JsonInclude]
    public MeaningData? Data { get; internal set; }
}
