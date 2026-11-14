using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nyra.Entities;

/// <summary>One audio track of a multi-audio YouTube video, such as a dubbed language.</summary>
/// <remarks>NodeLink reports them.</remarks>
public sealed record YouTubeAudioTrack
{
    /// <summary>Identifier to pass as <see cref="PlayOptions.AudioTrackId"/>.</summary>
    /// <remarks>
    /// Can be empty, do not pass an empty id on.
    /// </remarks>
    [JsonPropertyName("id"), JsonInclude]
    public string Id { get; internal set; } = string.Empty;

    /// <summary>Display name, such as the language of the dub.</summary>
    /// <remarks>Null when the server sent none.</remarks>
    [JsonPropertyName("name"), JsonInclude]
    public string? Name { get; internal set; }

    /// <summary>Whether the track plays when none is selected.</summary>
    /// <remarks>A null reads as false.</remarks>
    [JsonPropertyName("isDefault"), JsonInclude]
    public bool IsDefault { get; internal set; }

    /// <summary>Whether the track was auto-dubbed by the platform rather than uploaded.</summary>
    /// <remarks>A null reads as false.</remarks>
    [JsonPropertyName("isAutoDubbed"), JsonInclude]
    public bool IsAutoDubbed { get; internal set; }
}

/// <summary>Reads the YouTube audio tracks NodeLink reports for a video.</summary>
public static class YouTubeTrackExtensions
{
    /// <summary>
    /// The audio tracks NodeLink reports for a YouTube video, read out of a JSON string in <see
    /// cref="Track.PluginInfo"/>.
    /// </summary>
    /// <remarks>
    /// Null when there is nothing to read (like the track is not from YouTube), empty when the video has no
    /// alternate audio tracks.
    /// </remarks>
    /// <param name="track">The track to read the audio tracks from.</param>
    /// <returns>
    /// The video's alternate audio tracks, null when the track has none to read.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
    public static IReadOnlyList<YouTubeAudioTrack>? GetYouTubeAudioTracks(this Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
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
            var tracks = NyraJson.Deserialize<List<YouTubeAudioTrack>>(
                json,
                NyraJson.SerializerOptions
            );
            return tracks is null ? null : [.. tracks.Where(t => t is { Id: not null })];
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
