using System.Text.Json;
using System.Text.Json.Serialization;
using Nyra.Entities;

namespace Nyra.Internal;

/// <summary>
/// Converts <see cref="LoadResult"/> payloads for one server kind. NodeLink reports extra
/// load types (episode, album, artist, station, podcast, show), which map onto
/// track/playlist results, and places load errors at the top level instead of inside
/// <c>data</c>. A load type the library does not know degrades into a load failure.
/// </summary>
internal sealed class LoadResultConverter(bool isNodeLink) : JsonConverter<LoadResult>
{
    private const int TypeTrack = 1;
    private const int TypePlaylist = 2;
    private const int TypeSearch = 3;
    private const int TypeEmpty = 4;
    private const int TypeError = 5;

    public override LoadResult Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Load result must be an object.");

        var loadType = 0;
        string? unknownLoadType = null;
        var data = default(Utf8JsonReader);
        var exception = default(Utf8JsonReader);
        var hasData = false;
        var hasException = false;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("loadType"u8))
            {
                reader.Read();
                loadType = ReadLoadType(ref reader);
                if (loadType == 0)
                    unknownLoadType = reader.GetString(); // cold path: a newer server added a type
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                reader.Read();
                data = reader; // copy positioned at the value
                hasData = true;
                reader.Skip();
            }
            else if (reader.ValueTextEquals("exception"u8))
            {
                reader.Read();
                exception = reader; // copy positioned at the value
                hasException = true;
                reader.Skip();
            }
            else
            {
                reader.Skip();
            }
        }

        switch (loadType)
        {
            case TypeTrack:
            {
                if (!hasData)
                    throw new JsonException("Track load result is missing its 'data' member.");

                var track =
                    JsonSerializer.Deserialize<Track>(ref data, options)
                    ?? throw new JsonException("Track load result 'data' member is null.");
                return new LoadResult.TrackLoaded(track);
            }

            case TypePlaylist:
            {
                if (!hasData || data.TokenType != JsonTokenType.StartObject)
                    throw new JsonException("Playlist load result is missing its 'data' object.");

                return ReadPlaylist(ref data, options);
            }

            case TypeSearch:
            {
                if (!hasData)
                    throw new JsonException("Search load result is missing its 'data' member.");

                var tracks = JsonSerializer.Deserialize<List<Track>>(ref data, options) ?? [];
                return new LoadResult.SearchLoaded(tracks);
            }

            case TypeEmpty:
                return new LoadResult.NoMatches();

            case TypeError:
            {
                // Lavalink puts the exception inside "data", NodeLink places it at the top level.
                if (isNodeLink)
                {
                    if (!hasException || exception.TokenType != JsonTokenType.StartObject)
                        throw new JsonException("Error load result is missing its exception.");

                    return new LoadResult.LoadFailed(
                        JsonSerializer.Deserialize<ErrorInfo>(ref exception, options)
                            ?? throw new JsonException("Error load result exception is null.")
                    );
                }

                if (!hasData || data.TokenType != JsonTokenType.StartObject)
                    throw new JsonException("Error load result is missing its exception.");

                return new LoadResult.LoadFailed(
                    JsonSerializer.Deserialize<ErrorInfo>(ref data, options)
                        ?? throw new JsonException("Error load result exception is null.")
                );
            }

            default:
            {
                return new LoadResult.LoadFailed(
                    new ErrorInfo
                    {
                        Message =
                            $"The server reported an unknown load type '{unknownLoadType ?? "missing"}'.",
                    }
                );
            }
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        LoadResult value,
        JsonSerializerOptions options
    ) => throw new JsonException("LoadResult is a server response shape and is not serialized.");

    private int ReadLoadType(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("track"u8))
            return TypeTrack;
        if (reader.ValueTextEquals("playlist"u8))
            return TypePlaylist;
        if (reader.ValueTextEquals("search"u8))
            return TypeSearch;
        if (reader.ValueTextEquals("empty"u8))
            return TypeEmpty;
        if (reader.ValueTextEquals("error"u8))
            return TypeError;

        if (!isNodeLink)
            return 0;

        // NodeLink's extra types: episode behaves like a track, the rest like playlists.
        if (reader.ValueTextEquals("episode"u8))
            return TypeTrack;
        if (
            reader.ValueTextEquals("album"u8)
            || reader.ValueTextEquals("artist"u8)
            || reader.ValueTextEquals("station"u8)
            || reader.ValueTextEquals("podcast"u8)
            || reader.ValueTextEquals("show"u8)
        )
            return TypePlaylist;

        return 0;
    }

    private static LoadResult ReadPlaylist(ref Utf8JsonReader data, JsonSerializerOptions options)
    {
        var name = string.Empty;
        var selectedTrack = -1;
        List<Track>? tracks = null;
        JsonElement? pluginInfo = null;

        while (data.Read() && data.TokenType != JsonTokenType.EndObject)
        {
            var member = data; // copy at the property name
            data.Read(); // move to the value

            if (member.ValueTextEquals("info"u8) && data.TokenType == JsonTokenType.StartObject)
            {
                var info = data;
                while (info.Read() && info.TokenType != JsonTokenType.EndObject)
                {
                    var infoMember = info; // copy at the property name
                    info.Read(); // move to the value
                    if (
                        infoMember.ValueTextEquals("name"u8)
                        && info.TokenType == JsonTokenType.String
                    )
                        name = info.GetString() ?? string.Empty;
                    else if (
                        infoMember.ValueTextEquals("selectedTrack"u8)
                        && info.TokenType == JsonTokenType.Number
                        && info.TryGetInt32(out var selected)
                    )
                        selectedTrack = selected;
                    else
                        info.Skip();
                }

                data.Skip(); // consume the info object
            }
            else if (
                member.ValueTextEquals("tracks"u8)
                && data.TokenType == JsonTokenType.StartArray
            )
            {
                tracks = JsonSerializer.Deserialize<List<Track>>(ref data, options);
            }
            else if (member.ValueTextEquals("pluginInfo"u8))
            {
                if (data.TokenType == JsonTokenType.StartObject)
                    pluginInfo = JsonSerializer.Deserialize<JsonElement>(ref data, options);
                else
                    data.Skip();
            }
            else
            {
                data.Skip();
            }
        }

        return new LoadResult.PlaylistLoaded(
            new PlaylistInfo { Name = name, SelectedTrack = selectedTrack },
            tracks ?? [],
            pluginInfo
        );
    }
}
