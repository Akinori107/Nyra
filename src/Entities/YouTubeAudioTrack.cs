using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>One audio track of a multi-audio YouTube video, such as a dubbed language. NodeLink reports them.</summary>
public sealed record YouTubeAudioTrack
{
    /// <summary>Identifier to pass as <see cref="PlayOptions.AudioTrackId"/>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name, such as the language of the dub.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Whether the track plays when none is selected.</summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; init; }

    /// <summary>Whether the track was auto-dubbed by the platform rather than uploaded.</summary>
    [JsonPropertyName("isAutoDubbed")]
    public bool IsAutoDubbed { get; init; }
}

public static class YouTubeTrackExtensions
{
    /// <summary>
    /// The audio tracks NodeLink reports for a YouTube video, read out of a JSON string in
    /// <see cref="Track.PluginInfo"/>. Null when there is no plugin info to read from, e.g. the
    /// track isn't from YouTube; empty when NodeLink reported the video has no alternate audio
    /// tracks.
    /// </summary>
    public static IReadOnlyList<YouTubeAudioTrack>? GetYouTubeAudioTracks(this Track track)
    {
        if (track.PluginInfo is not { } pluginInfo)
            return null;

        if (
            pluginInfo.ValueKind != JsonValueKind.Object
            || !pluginInfo.TryGetProperty("audioTracks", out var raw)
            || raw.ValueKind != JsonValueKind.String
            || raw.GetString() is not { } json
        )
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<YouTubeAudioTrack>>(
                json,
                NyraJson.SerializerOptions
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
