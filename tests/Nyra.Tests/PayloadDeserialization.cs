using System.Text.Json;
using Nyra.Entities;

namespace Nyra.Tests;

public sealed class PayloadDeserialization
{
    [Test]
    public async Task AllPayloads()
    {
        var options = NyraJson.SerializerOptions;
        await PlayerPayload(options).ConfigureAwait(false);
        await LyricsPayload(options).ConfigureAwait(false);
        await GroupPayload(options).ConfigureAwait(false);
        await TrackPayload(options).ConfigureAwait(false);
        await LoadResultPayload(options).ConfigureAwait(false);
    }

    private static async Task PlayerPayload(JsonSerializerOptions options)
    {
        var update = JsonSerializer.Deserialize<Player>(
            """{"guildId":5,"state":{"time":45000,"position":1500,"connected":true,"ping":42},"volume":100}""",
            options
        );
        await Assert
            .That(update!.State!.Time)
            .IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(45000));
        await Assert.That(update.State.Position).IsEqualTo(TimeSpan.FromMilliseconds(1500));
        var edge = JsonSerializer.Deserialize<Player>(
            """{"guildId":5,"state":{"time":-999999999999999,"position":999999999999999,"connected":true,"ping":42},"volume":100}""",
            options
        );
        await Assert.That(edge!.State!.Time).IsEqualTo(DateTimeOffset.MinValue);
        await Assert.That(edge.State.Position).IsEqualTo(TimeSpan.Zero);

        var chainA = JsonSerializer.Deserialize<FilterChain>(
            """{"volume":100,"karaoke":{"level":0.5},"pluginFilters":{"x":{"a":1}}}""",
            options
        );
        var chainB = JsonSerializer.Deserialize<FilterChain>(
            """{"volume":100,"karaoke":{"level":0.5},"pluginFilters":{"x":{"a":1}}}""",
            options
        );
        var chainC = JsonSerializer.Deserialize<FilterChain>(
            """{"volume":100,"karaoke":{"level":0.9},"pluginFilters":{"x":{"a":2}}}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(chainA).IsEqualTo(chainB);
            await Assert.That(chainA).IsNotEqualTo(chainC);
            await Assert.That(chainA!.Equals(null)).IsFalse();
            await Assert.That(chainA!.GetHashCode()).IsEqualTo(chainB!.GetHashCode());
        }

        var trackUpdateA = JsonSerializer.Deserialize<PlayerUpdateTrack>(
            """{"encoded":"AAA","audioTrackId":"id1","userData":{"k":"v"}}""",
            options
        );
        var trackUpdateB = JsonSerializer.Deserialize<PlayerUpdateTrack>(
            """{"encoded":"AAA","audioTrackId":"id1","userData":{"k":"v"}}""",
            options
        );
        var trackUpdateC = JsonSerializer.Deserialize<PlayerUpdateTrack>(
            """{"encoded":"BBB","audioTrackId":"id2","userData":{"k":"w"}}""",
            options
        );
        var trackUpdateNoUser = JsonSerializer.Deserialize<PlayerUpdateTrack>(
            """{"encoded":"AAA","audioTrackId":"id1"}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(trackUpdateA).IsEqualTo(trackUpdateB);
            await Assert.That(trackUpdateA).IsNotEqualTo(trackUpdateC);
            await Assert.That(trackUpdateA).IsNotEqualTo(trackUpdateNoUser);
            await Assert.That(trackUpdateA!.Equals(null)).IsFalse();
            await Assert.That(trackUpdateA!.GetHashCode()).IsEqualTo(trackUpdateB!.GetHashCode());
        }

        var idUpdateA = JsonSerializer.Deserialize<PlayerIdentifierUpdateTrack>(
            """{"identifier":"song","userData":{"k":1}}""",
            options
        );
        var idUpdateB = JsonSerializer.Deserialize<PlayerIdentifierUpdateTrack>(
            """{"identifier":"song","userData":{"k":1}}""",
            options
        );
        var idUpdateC = JsonSerializer.Deserialize<PlayerIdentifierUpdateTrack>(
            """{"identifier":"other","userData":{"k":2}}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(idUpdateA).IsEqualTo(idUpdateB);
            await Assert.That(idUpdateA).IsNotEqualTo(idUpdateC);
            await Assert.That(idUpdateA!.Equals(null)).IsFalse();
            await Assert.That(idUpdateA!.GetHashCode()).IsEqualTo(idUpdateB!.GetHashCode());
        }

        var userData = JsonSerializer.Deserialize<JsonElement>("""{"z":9}""");
        PlayOptions playA = new(AudioTrackId: "a", UserData: userData);
        PlayOptions playB = new(AudioTrackId: "a", UserData: userData);
        PlayOptions playC = new(AudioTrackId: "b");
        PlayOptions playNoUser = new(AudioTrackId: "a");
        using (Assert.Multiple())
        {
            await Assert.That(playA).IsEqualTo(playB);
            await Assert.That(playA).IsNotEqualTo(playC);
            await Assert.That(playA).IsNotEqualTo(playNoUser);
            await Assert.That(playA.Equals(null)).IsFalse();
            await Assert.That(playA!.GetHashCode()).IsEqualTo(playB.GetHashCode());
        }
    }

    private static async Task LyricsPayload(JsonSerializerOptions options)
    {
        var lineA = JsonSerializer.Deserialize<LyricsLine>(
            """{"text":"hey","time":1000,"duration":500,"words":[{"word":"hey","start":1000,"end":1500}]}""",
            options
        );
        var lineB = JsonSerializer.Deserialize<LyricsLine>(
            """{"text":"hey","time":1000,"duration":500,"words":[{"word":"hey","start":1000,"end":1500}]}""",
            options
        );
        var lineC = JsonSerializer.Deserialize<LyricsLine>(
            """{"text":"yo","time":2000,"duration":100,"words":null}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(lineA).IsEqualTo(lineB);
            await Assert.That(lineA).IsNotEqualTo(lineC);
            await Assert.That(lineA!.Equals(null)).IsFalse();
            await Assert.That(lineA!.GetHashCode()).IsEqualTo(lineB!.GetHashCode());
        }

        var dataA = JsonSerializer.Deserialize<LyricsData>(
            """{"name":"N","synced":true,"provider":"p","lines":[{"text":"hey","time":1,"duration":1}],"text":"t","message":"m","severity":"info"}""",
            options
        );
        var dataB = JsonSerializer.Deserialize<LyricsData>(
            """{"name":"N","synced":true,"provider":"p","lines":[{"text":"hey","time":1,"duration":1}],"text":"t","message":"m","severity":"info"}""",
            options
        );
        var dataC = JsonSerializer.Deserialize<LyricsData>(
            """{"name":"M","synced":false,"provider":"q","lines":[],"text":"x","message":null,"severity":"warning"}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(dataA).IsEqualTo(dataB);
            await Assert.That(dataA).IsNotEqualTo(dataC);
            await Assert.That(dataA!.Equals(null)).IsFalse();
            await Assert.That(dataA!.GetHashCode()).IsEqualTo(dataB!.GetHashCode());
        }

        var resultA = JsonSerializer.Deserialize<LyricsResult>(
            """{"loadType":"LYRICS_LOADED","data":{"name":"N","synced":true,"lines":[]}}""",
            options
        );
        var resultB = JsonSerializer.Deserialize<LyricsResult>(
            """{"loadType":"LYRICS_LOADED","data":{"name":"N","synced":true,"lines":[]}}""",
            options
        );
        var resultC = JsonSerializer.Deserialize<LyricsResult>(
            """{"loadType":"LYRICS_NOT_FOUND","data":null}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(resultA).IsEqualTo(resultB);
            await Assert.That(resultA).IsNotEqualTo(resultC);
            await Assert.That(resultA!.Equals(null)).IsFalse();
            await Assert.That(resultA!.GetHashCode()).IsEqualTo(resultB!.GetHashCode());
        }
    }

    private static async Task GroupPayload(JsonSerializerOptions options)
    {
        var group = JsonSerializer.Deserialize<Group>(
            """{"id":null,"guildIds":[1,2],"createdAt":45000}""",
            options
        );
        await Assert.That(group!.Id).IsEqualTo(string.Empty);
        await Assert
            .That(group.CreatedAtTime)
            .IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(45000));
        var groupEcho = JsonSerializer.Deserialize<Group>(
            """{"id":null,"guildIds":[1,2],"createdAt":45000}""",
            options
        );
        await Assert.That(group.GetHashCode()).IsEqualTo(groupEcho!.GetHashCode());
        var edge = JsonSerializer.Deserialize<Group>(
            """{"id":"x","guildIds":[],"createdAt":999999999999999}""",
            options
        );
        await Assert.That(edge!.CreatedAtTime).IsEqualTo(DateTimeOffset.MinValue);

        var membershipA = JsonSerializer.Deserialize<GroupMembership>(
            """{"add":[1,2],"remove":[3]}""",
            options
        );
        var membershipB = JsonSerializer.Deserialize<GroupMembership>(
            """{"add":[1,2],"remove":[3]}""",
            options
        );
        var membershipC = JsonSerializer.Deserialize<GroupMembership>(
            """{"add":[],"remove":[]}""",
            options
        );
        using (Assert.Multiple())
        {
            await Assert.That(membershipA).IsEqualTo(membershipB);
            await Assert.That(membershipA).IsNotEqualTo(membershipC);
            await Assert.That(membershipA!.GetHashCode()).IsEqualTo(membershipB!.GetHashCode());
        }
    }

    private static async Task TrackPayload(JsonSerializerOptions options)
    {
        var info = JsonSerializer.Deserialize<TrackInfo>(
            """{"identifier":"i","author":"a","title":"t","length":90000,"isStream":false,"isSeekable":true,"position":5000}""",
            options
        );
        await Assert.That(info!.Length).IsEqualTo(TimeSpan.FromSeconds(90));
        await Assert.That(info.Position).IsEqualTo(TimeSpan.FromSeconds(5));
        var edge = JsonSerializer.Deserialize<TrackInfo>(
            """{"identifier":"i","author":"a","title":"t","length":0,"isStream":false,"isSeekable":true,"position":-3}""",
            options
        );
        await Assert.That(edge!.Length).IsEqualTo(TimeSpan.Zero);
        await Assert.That(edge.Position).IsEqualTo(TimeSpan.Zero);

        var bare = JsonSerializer.Deserialize<Track>("""{"encoded":null,"info":null}""", options);
        using (Assert.Multiple())
        {
            await Assert.That(bare!.Encoded).IsEqualTo(string.Empty);
            await Assert.That(bare.Info).IsNotNull();
            await Assert.That(bare.ToString()).IsEqualTo(bare.Info!.ToString());
        }
    }

    private static async Task LoadResultPayload(JsonSerializerOptions options)
    {
        var lava = NyraJson.OptionsFor(false);
        var trackJson =
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"a","title":"t","length":1000,"isStream":false,"isSeekable":true,"position":0}}""";
        var playlistJsonA =
            "{\"loadType\":\"playlist\",\"data\":{\"info\":{\"name\":\"p\",\"selectedTrack\":-1},\"tracks\":["
            + trackJson
            + "],\"pluginInfo\":{\"x\":1}}}";
        var playlistJsonB =
            "{\"loadType\":\"playlist\",\"data\":{\"info\":{\"name\":\"p\",\"selectedTrack\":-1},\"tracks\":["
            + trackJson
            + "],\"pluginInfo\":{\"x\":1}}}";
        var playlistA = JsonSerializer.Deserialize<LoadResult>(playlistJsonA, lava);
        var playlistB = JsonSerializer.Deserialize<LoadResult>(playlistJsonB, lava);
        var playlistC = JsonSerializer.Deserialize<LoadResult>(
            """{"loadType":"playlist","data":{"info":{"name":"q","selectedTrack":-1},"tracks":[],"pluginInfo":{"x":2}}}""",
            lava
        );
        using (Assert.Multiple())
        {
            await Assert.That(playlistA).IsEqualTo(playlistB);
            await Assert.That(playlistA).IsNotEqualTo(playlistC);
            await Assert.That(playlistA!.Equals(null)).IsFalse();
            await Assert.That(playlistA!.GetHashCode()).IsEqualTo(playlistB!.GetHashCode());
        }

        var searchJsonA = "{\"loadType\":\"search\",\"data\":[" + trackJson + "]}";
        var searchJsonB = "{\"loadType\":\"search\",\"data\":[" + trackJson + "]}";
        var searchA = JsonSerializer.Deserialize<LoadResult>(searchJsonA, lava);
        var searchB = JsonSerializer.Deserialize<LoadResult>(searchJsonB, lava);
        var searchC = JsonSerializer.Deserialize<LoadResult>(
            """{"loadType":"search","data":[]}""",
            lava
        );
        using (Assert.Multiple())
        {
            await Assert.That(searchA).IsEqualTo(searchB);
            await Assert.That(searchA).IsNotEqualTo(searchC);
            await Assert.That(searchA!.Equals(null)).IsFalse();
            await Assert.That(searchA!.GetHashCode()).IsEqualTo(searchB!.GetHashCode());
        }
    }
}
