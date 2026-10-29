using System.Net.WebSockets;
using System.Text.Json;
using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.EventArgs;
using Nyra.Exceptions;

namespace Nyra.Tests;

public sealed class Entity
{
    private static readonly JsonSerializerOptions NodeLink = NyraJson.OptionsFor(true);
    private static readonly JsonSerializerOptions Lavalink = NyraJson.OptionsFor(false);

    private static T? Parse<T>(string json, bool isNodeLink = true)
        where T : class => JsonSerializer.Deserialize<T>(json, NyraJson.OptionsFor(isNodeLink));

    private static T? ParseLava<T>(string json)
        where T : class => JsonSerializer.Deserialize<T>(json, Lavalink);

    [Test]
    public async Task Groups()
    {
        var group = Parse<Group>(
            """{"id":"late-night","guildIds":["123","456"],"createdAt":1699999999999}"""
        );
        using (Assert.Multiple())
        {
            await Assert.That(group).IsNotNull();
            await Assert.That(group).IsNotNull();
            await Assert.That(group!.Id).IsEqualTo("late-night");
            await Assert.That(group!.CreatedAt).IsEqualTo(1699999999999);
            await Assert.That(group!.GuildIds.Count).IsEqualTo(2);
        }
        await Assert.That(group!.GuildIds[1]).IsEqualTo(456);
        await Assert
            .That(group?.CreatedAtTime)
            .IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1699999999999));

        var hostile = Parse<Group>(
            """{"id":null,"guildIds":["123",42,"not-a-snowflake",[7]],"createdAt":99999999999999999}"""
        );
        await Assert.That(hostile is { Id: "", GuildIds: [123, 42] }).IsTrue();
        await Assert
            .That(Parse<Group>("""{"id":"x","guildIds":null}""") is { GuildIds.Count: 0 })
            .IsTrue();
        await Assert.That(hostile?.CreatedAtTime).IsEqualTo(DateTimeOffset.MinValue);

        var twice = Parse<Group>(
            """{"id":"late-night","guildIds":["123","456"],"createdAt":1699999999999}"""
        );
        await Assert.That(group == twice).IsTrue();
        twice = Parse<Group>("""{"id":"other","guildIds":["123"],"createdAt":1}""");
        await Assert.That(group != twice).IsTrue();

        var update = Parse<GroupUpdateResult>(
            """
            {"id":"late-night","guildIds":["123","456"],"createdAt":1699999999999,
             "players":[{"guildId":"123","volume":80,"paused":true},{"guildId":"456"},
                        null,{"guildId":"789","paused":false}],
             "errors":[{"guildId":"789","error":"Player not found locally"},
                       {"guildId":"999","error":null},null]}
            """
        );
        using (Assert.Multiple())
        {
            await Assert.That(update).IsNotNull();
            await Assert.That(update).IsNotNull();
            await Assert.That(update!.Id).IsEqualTo("late-night");
            await Assert.That(update!.GuildIds.Count).IsEqualTo(2);
            await Assert.That(update?.Players.Count).IsEqualTo(3);
        }
        await Assert
            .That(update!.Players[0] is { GuildId: 123, Volume: 80, IsPaused: true })
            .IsTrue();
        await Assert.That(update!.Players[2] is { GuildId: 789, Volume: 0 }).IsTrue();
        await Assert.That(update?.Errors.Count).IsEqualTo(2);
        await Assert
            .That(update!.Errors[0] is { GuildId: 789, Error: "Player not found locally" })
            .IsTrue();
        await Assert.That(update!.Errors[1] is { GuildId: 999, Error: "" }).IsTrue();
        await Assert.That(update?.Errors[0].Error).IsEqualTo("Player not found locally");
        await Assert
            .That(
                Parse<GroupGuildError>("""{"guildId":"1","error":"x"}""")
                    == Parse<GroupGuildError>("""{"guildId":"1","error":"x"}""")
            )
            .IsTrue();

        var noLists = Parse<GroupUpdateResult>("""{"id":"solo","guildIds":[],"createdAt":5}""");
        using (Assert.Multiple())
        {
            await Assert.That(noLists).IsNotNull();
            await Assert.That(noLists!.Players.Count).IsEqualTo(0);
            await Assert.That(noLists!.Errors.Count).IsEqualTo(0);
        }

        var body = JsonSerializer.Serialize(
            new GroupUpdate
            {
                Players = new GroupMembership { Add = [123], Remove = [456] },
                Track = new GroupTrack { Encoded = "QXRsZWFzdA==" },
                PositionMilliseconds = 30_000,
                Volume = 250,
                Paused = true,
            },
            NodeLink
        );
        await Assert
            .That(body)
            .IsEqualTo(
                """
                {"players":{"add":["123"],"remove":["456"]},"track":{"encoded":"QXRsZWFzdA=="},"position":30000,"volume":250,"paused":true}
                """
            );

        var stop = JsonSerializer.Serialize(
            new GroupUpdate { Track = GroupTrack.Stop, EndTimeMilliseconds = 60_000 },
            NodeLink
        );
        await Assert.That(stop).IsEqualTo("""{"track":{"encoded":null},"endTime":60000}""");

        var identifier = JsonSerializer.Serialize(
            new GroupUpdate { Track = new GroupTrack { Identifier = "ytsearch:atwa" } },
            NodeLink
        );
        await Assert.That(identifier).IsEqualTo("""{"track":{"identifier":"ytsearch:atwa"}}""");

        var cleared = JsonSerializer.Serialize(new GroupUpdate(), NodeLink);
        await Assert.That(cleared).IsEqualTo("{}");
    }

    [Test]
    public async Task Lyrics()
    {
        var result = Parse<LyricsResult>(
            """
            {"loadType":"lyrics","data":{"name":"ATWA","synced":true,
             "lines":[{"text":"first","time":1000,"duration":2000,"words":[{"w":"first","s":1000}]},
                      {"text":"second","time":3000,"duration":0,"words":null},null],
             "provider":"musixmatch"}}
            """
        );
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.LoadType).IsEqualTo(LyricsLoadType.Lyrics);
        await Assert
            .That(result?.Data is { Name: "ATWA", Synced: true, Provider: "musixmatch" })
            .IsTrue();
        await Assert.That(result?.Data?.Lines is { Count: 2 }).IsTrue();
        await Assert
            .That(result?.Data?.Lines?[0] is { Time: 1000, Duration: 2000, Text: "first" })
            .IsTrue();
        await Assert.That(result?.Data?.Lines?[1] is { Duration: 0, Text: "second" }).IsTrue();
        await Assert
            .That(
                result?.Data?.Lines?[0].Words is { } words && words.ValueKind == JsonValueKind.Array
            )
            .IsTrue();

        var empty = Parse<LyricsResult>("""{"loadType":"empty","data":{}}""");
        using (Assert.Multiple())
        {
            await Assert.That(empty).IsNotNull();
            await Assert.That(empty!.LoadType).IsEqualTo(LyricsLoadType.Empty);
            await Assert.That(empty!.Data).IsNotNull();
            await Assert.That(empty!.Data!.Lines).IsNull();
        }

        var error = Parse<LyricsResult>(
            """{"loadType":"error","data":{"message":"no provider","severity":"suspicious"}}"""
        );
        using (Assert.Multiple())
        {
            await Assert.That(error).IsNotNull();
            await Assert.That(error!.LoadType).IsEqualTo(LyricsLoadType.Error);
            await Assert.That(error!.Data).IsNotNull();
            await Assert.That(error!.Data!.Message).IsEqualTo("no provider");
            await Assert.That(error!.Data!.Severity).IsEqualTo(ErrorSeverity.Suspicious);
        }

        var unknown = Parse<LyricsResult>("""{"loadType":"brand-new","data":{}}""");
        await Assert.That(unknown).IsNotNull();
        await Assert.That(unknown!.LoadType).IsEqualTo(LyricsLoadType.Unknown);

        await Assert
            .That(
                result
                    == Parse<LyricsResult>(
                        """
                        {"loadType":"lyrics","data":{"name":"ATWA","synced":true,
                         "lines":[{"text":"first","time":1000,"duration":2000,"words":[{"w":"first","s":1000}]},
                                  {"text":"second","time":3000,"duration":0,"words":null},null],
                         "provider":"musixmatch"}}
                        """
                    )
            )
            .IsTrue();

        var lava = JsonSerializer.Deserialize<LavaLyricsLyrics>(
            """{"sourceName":"netease","provider":"NetEase","text":"plain text","lines":[{"timestamp":1000,"duration":2000,"line":"line one"},null]}""",
            Lavalink
        )!;
        var projected = lava.ToResult();
        using (Assert.Multiple())
        {
            await Assert.That(projected).IsNotNull();
            await Assert.That(projected!.LoadType).IsEqualTo(LyricsLoadType.Lyrics);
            await Assert.That(projected!.Data).IsNotNull();
            await Assert.That(projected!.Data!.Synced).IsTrue();
            await Assert.That(projected!.Data!.Lines).IsNotNull();
            await Assert.That(projected!.Data!.Lines!.Count).IsEqualTo(1);
        }
        await Assert
            .That(projected!.Data!.Lines![0] is { Text: "line one", Time: 1000, Duration: 2000 })
            .IsTrue();

        var lavaBare = new LavaLyricsLyrics("genius", null, null, []);
        await Assert
            .That(lavaBare.ToResult().Data is { Synced: false, Lines: { Count: 0 } })
            .IsTrue();

        var lavaNone = new LavaLyricsLyrics("genius", null, null, null);
        await Assert.That(lavaNone.ToResult().Data is { Synced: false, Lines: null }).IsTrue();
    }

    [Test]
    public async Task Meanings()
    {
        var result = Parse<MeaningResult>(
            """
            {"loadType":"meaning","data":{
             "title":"About ATWA","description":"A song about air, trees, water, animals.",
             "paragraphs":["first paragraph",null,"third paragraph"],
             "url":"https://example.com/atwa","type":"track","provider":"wikipedia",
             "translation":{"language":{"source":"en","target":"pt"},
                            "title":"Sobre ATWA","description":"Uma música",
                            "paragraphs":["primeiro parágrafo"]},
             "meaningMeta":{"id":123,"localeId":"en-US","origin":"user","submittedBy":"someone",
                            "reviewedBy":"other"},
             "song":{"title":"ATWA","artist":"System of a Down","youtubeId":"ph8qt3dhvwo",
                     "letrasId":"42","artworkUrl":"https://example.com/a.jpg"}}}
            """
        );
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.LoadType).IsEqualTo(MeaningLoadType.Meaning);
        await Assert
            .That(
                result?.Data is { Title: "About ATWA", Provider: "wikipedia", Type: "track" }
                    && result.Data.Paragraphs.Count == 2
            )
            .IsTrue();
        await Assert
            .That(
                result?.Data?.Translation is { Title: "Sobre ATWA" }
                    && result.Data.Translation.Paragraphs[0] == "primeiro parágrafo"
                    && result.Data.Translation.Language is { Source: "en", Target: "pt" }
            )
            .IsTrue();
        await Assert
            .That(result?.Data?.Meta is { Id: "123", LocaleId: "en-US", Origin: "user" })
            .IsTrue();
        await Assert
            .That(result?.Data?.Song is { Title: "ATWA", YoutubeId: "ph8qt3dhvwo", LetrasId: "42" })
            .IsTrue();

        var stringId = Parse<MeaningResult>(
            """{"loadType":"meaning","data":{"meaningMeta":{"id":"77"}}}"""
        );
        await Assert.That(stringId?.Data?.Meta?.Id).IsEqualTo("77");

        var failed = Parse<MeaningResult>(
            """{"loadType":"error","data":{"message":"provider down","severity":"fault"}}"""
        );
        using (Assert.Multiple())
        {
            await Assert.That(failed).IsNotNull();
            await Assert.That(failed!.LoadType).IsEqualTo(MeaningLoadType.Error);
            await Assert.That(failed!.Data).IsNotNull();
            await Assert.That(failed!.Data!.ErrorMessage).IsEqualTo("provider down");
            await Assert.That(failed!.Data!.ErrorSeverity).IsEqualTo(ErrorSeverity.Fault);
        }

        var empty = Parse<MeaningResult>("""{"loadType":"empty","data":{}}""");
        using (Assert.Multiple())
        {
            await Assert.That(empty).IsNotNull();
            await Assert.That(empty!.LoadType).IsEqualTo(MeaningLoadType.Empty);
            await Assert.That(empty!.Data).IsNotNull();
            await Assert.That(empty!.Data!.Paragraphs.Count).IsEqualTo(0);
        }

        await Assert
            .That(
                result
                    == Parse<MeaningResult>(
                        """
                        {"loadType":"meaning","data":{
                         "title":"About ATWA","description":"A song about air, trees, water, animals.",
                         "paragraphs":["first paragraph",null,"third paragraph"],
                         "url":"https://example.com/atwa","type":"track","provider":"wikipedia",
                         "translation":{"language":{"source":"en","target":"pt"},
                                        "title":"Sobre ATWA","description":"Uma música",
                                        "paragraphs":["primeiro parágrafo"]},
                         "meaningMeta":{"id":123,"localeId":"en-US","origin":"user","submittedBy":"someone",
                                        "reviewedBy":"other"},
                         "song":{"title":"ATWA","artist":"System of a Down","youtubeId":"ph8qt3dhvwo",
                                 "letrasId":"42","artworkUrl":"https://example.com/a.jpg"}}}
                        """
                    )
            )
            .IsTrue();
    }

    [Test]
    public async Task StatsDetail()
    {
        var stats = Parse<Stats>(
            """
            {"players":7,"playingPlayers":3,"uptime":-5,
             "memory":{"free":1,"used":2,"allocated":3,"reservable":4},
             "cpu":{"cores":4,"systemLoad":0.75,"lavalinkLoad":0.1,"nodelinkLoad":0.6},
             "detailedStats":{
               "api":{"requests":{"loadtracks":10,"others":2},"errors":{"loadtracks":1}},
               "sources":{"youtube":{"success":9,"failure":1},"spotify":null},
               "playback":{"events":{"TrackStartEvent":40,"TrackEndEvent":39}}}}
            """
        );
        await Assert.That(stats).IsNotNull();
        await Assert.That(stats?.UptimeDuration).IsEqualTo(TimeSpan.Zero);
        await Assert.That(stats?.Cpu is { NodeLoad: 0.6 }).IsTrue();
        await Assert
            .That(
                stats?.DetailedStats?.Api is { }
                    && stats.DetailedStats.Api.Requests?["loadtracks"] == 10
                    && stats.DetailedStats.Api.Errors?["loadtracks"] == 1
            )
            .IsTrue();
        await Assert
            .That(
                stats?.DetailedStats?.Sources is { Count: 1 } sources
                    && sources["youtube"] is { Success: 9, Failure: 1 }
            )
            .IsTrue();
        await Assert.That(stats?.DetailedStats?.Playback?.Events?["TrackStartEvent"]).IsEqualTo(40);
        await Assert
            .That(
                stats?.DetailedStats
                    == Parse<Stats>(
                        """
                        {"detailedStats":{
                          "api":{"requests":{"loadtracks":10,"others":2},"errors":{"loadtracks":1}},
                          "sources":{"youtube":{"success":9,"failure":1}},
                          "playback":{"events":{"TrackStartEvent":40,"TrackEndEvent":39}}}}
                        """
                    )?.DetailedStats
            )
            .IsTrue();

        var lava = Parse<Stats>(
            """{"players":1,"playingPlayers":1,"uptime":9001,"cpu":{"cores":2,"systemLoad":0.5,"lavalinkLoad":0.25}}""",
            isNodeLink: false
        );
        await Assert
            .That(lava is { UptimeDuration: { TotalMilliseconds: 9001 }, Cpu: { NodeLoad: 0.25 } })
            .IsTrue();
    }

    [Test]
    public async Task YouTubeSurface()
    {
        var config = Parse<YouTubeConfig>(
            """{"refreshToken":"***","visitorData":"***","isConfigured":true}"""
        );
        using (Assert.Multiple())
        {
            await Assert.That(config).IsNotNull();
            await Assert.That(config!.RefreshToken).IsEqualTo("***");
            await Assert.That(config!.VisitorData).IsEqualTo("***");
            await Assert.That(config!.IsConfigured).IsTrue();
            await Assert.That(config!.IsValid).IsNull();
        }

        var validated = Parse<YouTubeConfig>("""{"isConfigured":true,"isValid":false}""");
        await Assert.That(validated?.IsValid).IsFalse();

        var update = Parse<YouTubeConfigUpdate>(
            """{"message":"configuration updated","workersUpdated":4,"fieldsUpdated":["refreshToken",null]}"""
        );
        using (Assert.Multiple())
        {
            await Assert.That(update).IsNotNull();
            await Assert.That(update!.Message).IsEqualTo("configuration updated");
            await Assert.That(update!.WorkersUpdated).IsEqualTo(4);
            await Assert.That(update!.FieldsUpdated.Count).IsEqualTo(1);
        }

        var nullUpdate = Parse<YouTubeConfigUpdate>("""{"workersUpdated":0}""");
        using (Assert.Multiple())
        {
            await Assert.That(nullUpdate).IsNotNull();
            await Assert.That(nullUpdate!.Message).IsEqualTo("");
            await Assert.That(nullUpdate!.FieldsUpdated.Count).IsEqualTo(0);
        }

        var token = Parse<YouTubeToken>(
            """{"access_token":null,"expires_in":3600,"scope":"read","token_type":"Bearer"}"""
        );
        using (Assert.Multiple())
        {
            await Assert.That(token).IsNotNull();
            await Assert.That(token!.AccessToken).IsEqualTo("");
            await Assert.That(token!.ExpiresInSeconds).IsEqualTo(3600);
            await Assert.That(token!.TokenType).IsEqualTo("Bearer");
        }

        var refresh = JsonSerializer.Serialize(new YouTubeTokenRefresh("secret"), NodeLink);
        await Assert.That(refresh).IsEqualTo("""{"refreshToken":"secret"}""");

        var patch = JsonSerializer.Serialize(new YouTubeConfigPatch(), NodeLink);
        await Assert.That(patch).IsEqualTo("{}");
        var full = JsonSerializer.Serialize(
            new YouTubeConfigPatch(RefreshToken: "r", VisitorData: "v"),
            NodeLink
        );
        await Assert.That(full).IsEqualTo("""{"refreshToken":"r","visitorData":"v"}""");

        var track = Parse<Track>(
            """
            {"encoded":"QXRsZWFzdA==",
             "info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA",
                     "length":176000,"isStream":false,"isSeekable":true,"position":0},
             "pluginInfo":{"audioTracks":"[{\"id\":\"en.0\",\"name\":\"English\",\"isDefault\":true,\"isAutoDubbed\":false},{\"id\":null},{\"id\":\"es.0\",\"name\":\"Spanish\"}]"}}
            """
        );
        var audio = track?.GetYouTubeAudioTracks();
        await Assert.That(audio).IsNotNull();
        await Assert.That(audio!.Count).IsEqualTo(2);
        await Assert
            .That(
                audio![0] is { Id: "en.0", Name: "English", IsDefault: true, IsAutoDubbed: false }
            )
            .IsTrue();
        await Assert.That(audio![1] is { Id: "es.0", Name: "Spanish" }).IsTrue();

        var plain = Parse<Track>(
            """{"encoded":"QQ==","info":{"identifier":"x","author":"a","title":"t","length":1,"isStream":false,"isSeekable":true,"position":0}}"""
        );
        await Assert.That(plain?.GetYouTubeAudioTracks()).IsNull();

        var malformed = Parse<Track>(
            """{"encoded":"QQ==","info":{"identifier":"x","author":"a","title":"t","length":1,"isStream":false,"isSeekable":true,"position":0},"pluginInfo":{"audioTracks":"not json ["}}"""
        );
        await Assert.That(malformed?.GetYouTubeAudioTracks()).IsNull();

        var notString = Parse<Track>(
            """{"encoded":"QQ==","info":{"identifier":"x","author":"a","title":"t","length":1,"isStream":false,"isSeekable":true,"position":0},"pluginInfo":{"audioTracks":7}}"""
        );
        await Assert.That(notString?.GetYouTubeAudioTracks()).IsNull();

        Track? nullTrack = null;
        await Assert
            .That(() =>
            {
                _ = nullTrack!.GetYouTubeAudioTracks();
            })
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task StreamMeta()
    {
        var metadata = Parse<StreamMetadata>(
            """
            {"metadata":{"raw":"StreamTitle='x';","streamTitle":"x","streamUrl":"https://s.example",
                         "fields":{"streamtitle":"x","streamurl":null}},
             "icy":{"name":"Radio","description":"d","genre":"rock","url":"https://r.example","bitrate":"128"},
             "receivedAt":1699999999999}
            """
        );
        await Assert
            .That(
                metadata?.Metadata is { StreamTitle: "x", StreamUrl: "https://s.example" }
                    && metadata.Metadata.Fields?.Count == 1
                    && metadata.Metadata.Fields["streamtitle"] == "x"
            )
            .IsTrue();
        await Assert
            .That(metadata?.Icy is { Name: "Radio", Genre: "rock", Bitrate: "128" })
            .IsTrue();
        await Assert
            .That(metadata?.ReceivedAt)
            .IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1699999999999));

        var hostile = Parse<StreamMetadata>("""{"receivedAt":99999999999999999}""");
        await Assert.That(hostile?.ReceivedAt).IsEqualTo(DateTimeOffset.MinValue);

        var none = Parse<StreamMetadata>("""{}""");
        await Assert.That(none?.ReceivedAt).IsNull();
        await Assert
            .That(
                metadata?.Metadata
                    == Parse<StreamMetadata>(
                        """{"metadata":{"raw":"StreamTitle='x';","streamTitle":"x","streamUrl":"https://s.example","fields":{"streamurl":null,"streamtitle":"x"}}}"""
                    )?.Metadata
            )
            .IsTrue();
    }

    [Test]
    public async Task LoadFamily()
    {
        var trackJson =
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

        var trackLoad = ParseLava<LoadResult>(
            $$$$"""{"loadType":"track","data":{{{{trackJson}}}}}"""
        );
        await Assert
            .That(trackLoad is LoadResult.TrackLoaded { Track: { Encoded: "QXRsZWFzdA==" } })
            .IsTrue();

        var episode = Parse<LoadResult>($$$$"""{"loadType":"episode","data":{{{{trackJson}}}}}""");
        await Assert.That(episode is LoadResult.TrackLoaded).IsTrue();

        var search = ParseLava<LoadResult>(
            $$$$"""{"loadType":"search","data":[{{{{trackJson}}}},{{{{trackJson}}}}]}"""
        );
        await Assert.That(search is LoadResult.SearchLoaded { Tracks.Count: 2 }).IsTrue();

        var playlist = ParseLava<LoadResult>(
            $$$$"""{"loadType":"playlist","data":{"info":{"name":"SOAD radio","selectedTrack":1},"tracks":[{{{{trackJson}}}}],"pluginInfo":{"a":1}}}"""
        );
        await Assert
            .That(
                playlist
                    is LoadResult.PlaylistLoaded
                    {
                        PlaylistInfo: { Name: "SOAD radio", SelectedTrack: 1 },
                        Tracks.Count: 1,
                        PluginInfo: not null,
                    }
            )
            .IsTrue();
        await Assert
            .That(
                playlist
                    == ParseLava<LoadResult>(
                        $$$$"""{"loadType":"playlist","data":{"info":{"name":"SOAD radio","selectedTrack":1},"tracks":[{{{{trackJson}}}}],"pluginInfo":{"a":1}}}"""
                    )
            )
            .IsTrue();

        foreach (var kind in new[] { "album", "artist", "station", "podcast", "show" })
        {
            var like = Parse<LoadResult>(
                $$$$"""{"loadType":"{{{{kind}}}}","data":{"info":{"name":"n"},"tracks":[{{{{trackJson}}}}]}}"""
            );
            await Assert.That(like is LoadResult.PlaylistLoaded { Tracks.Count: 1 }).IsTrue();
        }

        var noMatches = ParseLava<LoadResult>("""{"loadType":"empty"}""");
        await Assert.That(noMatches is LoadResult.NoMatches).IsTrue();

        var lavaError = ParseLava<LoadResult>(
            """{"loadType":"error","data":{"message":"nope","severity":"fault","cause":"upstream","causeStackTrace":"at Lava"}}"""
        );
        await Assert
            .That(
                lavaError
                    is LoadResult.LoadFailed
                    {
                        Error:
                        { Message: "nope", Severity: ErrorSeverity.Fault, Cause: "upstream" },
                    }
            )
            .IsTrue();
        await Assert
            .That(
                lavaError is LoadResult.LoadFailed { Error: { } lavaErr }
                    && lavaErr.ToString() == "[Fault] nope"
            )
            .IsTrue();

        var nodeLinkError = Parse<LoadResult>(
            """{"loadType":"error","exception":{"message":"provider down"}}"""
        );
        await Assert
            .That(nodeLinkError is LoadResult.LoadFailed { Error: { Message: "provider down" } })
            .IsTrue();

        var unknown = Parse<LoadResult>("""{"loadType":"holo","data":{}}""");
        await Assert
            .That(
                unknown
                    is LoadResult.LoadFailed
                    {
                        Error: { Message: "The server reported an unknown load type 'holo'." },
                    }
            )
            .IsTrue();

        var notString = Parse<LoadResult>("""{"loadType":7}""");
        await Assert
            .That(
                notString
                    is LoadResult.LoadFailed
                    {
                        Error:
                        { Message: "The server reported an unknown load type 'not a string'." },
                    }
            )
            .IsTrue();

        var missing = ParseLava<LoadResult>("""{}""");
        await Assert
            .That(
                missing
                    is LoadResult.LoadFailed
                    {
                        Error: { Message: "The server reported an unknown load type 'missing'." },
                    }
            )
            .IsTrue();

        foreach (
            var frame in new string[]
            {
                """{"loadType":"track"}""",
                $$$$"""{"loadType":"track","data":null}""",
                $$$$"""{"loadType":"track","data":5}""",
                $$$$"""{"loadType":"search","data":null}""",
                $$$$"""{"loadType":"playlist"}""",
                $$$$"""{"loadType":"error"}""",
            }
        )
        {
            await Assert
                .That(() =>
                {
                    _ = ParseLava<LoadResult>(frame);
                })
                .Throws<JsonException>();
        }

        await Assert
            .That(() =>
            {
                _ = ParseLava<LoadResult>($$$$"""{"loadType":"search","data":[null]}""");
            })
            .Throws<JsonException>();

        var missingTracks = ParseLava<LoadResult>(
            $$$$"""{"loadType":"playlist","data":{"info":{"name":"n"}}}"""
        );
        var nullTracks = ParseLava<LoadResult>(
            $$$$"""{"loadType":"playlist","data":{"info":{"name":"n"},"tracks":null}}"""
        );
        await Assert
            .That(
                missingTracks
                    is LoadResult.PlaylistLoaded { Tracks.Count: 0, PlaylistInfo.Name: "n" }
            )
            .IsTrue();
        await Assert.That(nullTracks is LoadResult.PlaylistLoaded { Tracks.Count: 0 }).IsTrue();
    }

    [Test]
    public async Task ServerInfo()
    {
        var info = Parse<NodeInfo>(
            """
            {"version":{"semver":"3.9.1","major":3,"minor":9,"patch":1},
             "buildTime":1700000000000,
             "git":{"branch":"main","commit":"abc123","commitTime":1700000000000},
             "jvm":"OpenJDK 21","lavaplayer":"1.0.0","node":"v22.0.0",
             "voice":{"name":"udp","url":"udp://"},
             "isNodelink":false,
             "sourceManagers":["youtube","soundcloud"],
             "filters":["volume","karaoke"]}
            """
        );
        using (Assert.Multiple())
        {
            await Assert.That(info).IsNotNull();
            await Assert.That(info!.Version).IsNotNull();
            await Assert.That(info!.Version!.Semver).IsEqualTo("3.9.1");
            await Assert.That(info!.Version!.Major).IsEqualTo(3);
            await Assert.That(info!.Version!.Minor).IsEqualTo(9);
            await Assert.That(info!.Version!.Patch).IsEqualTo(1);
            await Assert.That(info!.Git).IsNotNull();
            await Assert.That(info!.Git!.Branch).IsEqualTo("main");
            await Assert.That(info!.Git!.Commit).IsEqualTo("abc123");
            await Assert.That(info!.Voice).IsNotNull();
            await Assert.That(info!.Voice!.Name).IsEqualTo("udp");
            await Assert.That(info?.Jvm).IsEqualTo("OpenJDK 21");
            await Assert.That(info?.Lavaplayer).IsEqualTo("1.0.0");
            await Assert.That(info?.Node).IsEqualTo("v22.0.0");
        }
        await Assert.That(info?.SourceManagers is { Count: 2 }).IsTrue();
        await Assert.That(info?.Filters is { Count: 2 }).IsTrue();
        await Assert
            .That(info?.BuildTime)
            .IsEqualTo(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));

        var planner = Parse<RoutePlannerStatus>(
            """
            {"class":"RotatingIpRoutePlanner",
             "details":{"ipBlock":{"type":"Inet4Address","size":"/8"},
                        "failingAddresses":[{"failingAddress":"/1.2.3.4","failingTimestamp":1700000000000,
                                              "failingTime":"Tue Nov 14 2023"},
                                             {"failingAddress":null,"failingTimestamp":0,"failingTime":null}]}}
            """
        );
        await Assert.That(planner).IsNotNull();
        await Assert.That(planner!.Class).IsEqualTo(RoutePlannerClass.Rotating);
        await Assert
            .That(planner?.Details?.IpBlock is { Type: IpBlockType.Inet4, Size: "/8" })
            .IsTrue();
        await Assert
            .That(
                planner?.Details?.FailingAddresses is { Count: 2 }
                    && planner.Details.FailingAddresses[0]
                        is { Address: "/1.2.3.4", FailingTime: "Tue Nov 14 2023" }
                    && planner.Details.FailingAddresses[0].FailingTimestamp
                        == DateTimeOffset.FromUnixTimeMilliseconds(1700000000000)
                    && planner.Details.FailingAddresses[1] is { Address: "", FailingTime: "" }
            )
            .IsTrue();

        var unknownPlanner = Parse<RoutePlannerStatus>(
            """{"class":"BrandNewPlanner","details":{"ipBlock":{"type":"Inet_99"}}}"""
        );
        using (Assert.Multiple())
        {
            await Assert.That(unknownPlanner).IsNotNull();
            await Assert.That(unknownPlanner!.Class).IsEqualTo(RoutePlannerClass.Unknown);
            await Assert
                .That(unknownPlanner!.Details?.IpBlock?.Type)
                .IsEqualTo(IpBlockType.Unknown);
        }

        var free = JsonSerializer.Serialize(new RoutePlannerFreeAddress("1.2.3.4"), NodeLink);
        await Assert.That(free).IsEqualTo("""{"address":"1.2.3.4"}""");
    }

    [Test]
    public async Task Exceptions()
    {
        var cause = new InvalidOperationException("the socket died");
        var unreachable = new NodeUnreachableException("the node never answered", cause);
        await Assert.That(unreachable.InnerException).IsSameReferenceAs(cause);
        await Assert.That(unreachable.Message).IsEqualTo("the node never answered");

        var incompatible = new IncompatibleServerException("no /v4/info", cause);
        await Assert.That(ReferenceEquals(incompatible.InnerException, cause)).IsTrue();

        var handshake = new VoiceHandshakeException("the voice update timed out");
        await Assert.That(handshake is NyraException { InnerException: null }).IsTrue();

        bool grouped;
        try
        {
            throw new VoiceHandshakeException("no voice");
        }
        catch (NyraException caught)
        {
            grouped = caught.Message == "no voice";
        }
        await Assert.That(grouped).IsTrue();
    }

    [Test]
    public async Task EventArgShapes()
    {
        var node = new Node(new Endpoint("127.0.0.1", 3000, "youshallnotpass"));

        var connected = new NodeConnectedEventArgs(node);
        await Assert.That(connected.Node).IsSameReferenceAs(node);

        var disconnected = new NodeDisconnectedEventArgs(
            WebSocketCloseStatus.NormalClosure,
            "bye",
            final: true,
            exception: null
        );
        using (Assert.Multiple())
        {
            await Assert.That(disconnected).IsNotNull();
            await Assert
                .That(disconnected!.CloseStatus)
                .IsEqualTo(WebSocketCloseStatus.NormalClosure);
            await Assert.That(disconnected!.CloseDescription).IsEqualTo("bye");
            await Assert.That(disconnected!.Final).IsTrue();
            await Assert.That(disconnected!.Exception).IsNull();
        }

        var error = new NodeErrorEventArgs(new IOException("reset"), node);
        await Assert.That(error.Exception is IOException).IsTrue();
        await Assert.That(error.Node).IsSameReferenceAs(node);

        var stats = Parse<Stats>("""{"players":1,"playingPlayers":1,"uptime":5}""");
        var statsArgs = new NodeStatsEventArgs(stats!);
        await Assert.That(statsArgs.Stats == stats).IsTrue();

        var quality = Parse<ConnectionStatus>(
            """{"status":"good","metrics":{"ping":10},"reason":null}"""
        );
        using (Assert.Multiple())
        {
            await Assert.That(quality).IsNotNull();
            await Assert.That(quality!.Status).IsEqualTo(ConnectionQuality.Good);
            await Assert.That(quality!.Reason).IsNull();
            await Assert.That(quality!.Metrics).IsNotNull();
        }
        await Assert
            .That(
                Parse<ConnectionStatus>("""{"status":null}""")
                    is { Status: ConnectionQuality.Unknown }
            )
            .IsTrue();
        await Assert
            .That(
                Parse<ConnectionStatus>("""{"status":"brand-new"}""")
                    is { Status: ConnectionQuality.Unknown }
            )
            .IsTrue();
        await Assert
            .That(
                Parse<ConnectionStatus>("""{"status":"disabled","reason":"no manager"}""")
                    is { Status: ConnectionQuality.Disabled, Reason: "no manager" }
            )
            .IsTrue();
        await Assert
            .That(quality == Parse<ConnectionStatus>("""{"status":"good","metrics":{"ping":10}}"""))
            .IsTrue();

        var statusArgs = new NodeConnectionStatusEventArgs(quality!);
        await Assert.That(statusArgs.Status == quality).IsTrue();

        var playbackError = new HandlerErrorEventArgs(
            new IOException("the track died mid-flight"),
            origin: statsArgs
        );
        await Assert.That(playbackError is { Exception: IOException, Origin: not null }).IsTrue();

        var invalidated = new GuildConnectionInvalidatedEventArgs(123, lastChannelId: 456);
        using (Assert.Multiple())
        {
            await Assert.That(invalidated).IsNotNull();
            await Assert.That(invalidated!.GuildId).IsEqualTo(123);
            await Assert.That(invalidated!.LastChannelId).IsEqualTo(456);
        }

        var sought = Parse<TrackSoughtEventArgs>(
            """{"op":"event","guildId":"123","code":"TrackSoughtEvent","position":30000}"""
        );
        await Assert
            .That(
                sought
                    is { GuildId: 123, PositionMilliseconds: 30000, Position: { TotalSeconds: 30 } }
            )
            .IsTrue();

        var box = Parse<EternalBoxInfoUpdatedEventArgs>(
            """{"op":"event","guildId":"123","eternalbox":{"mode":"loop"}}"""
        );
        using (Assert.Multiple())
        {
            await Assert.That(box).IsNotNull();
            await Assert.That(box!.GuildId).IsEqualTo(123);
            await Assert.That(box!.Info).IsNotNull();
        }
    }

    [Test]
    public async Task FilterRest()
    {
        var options = NodeLink;
        var chain = new FilterChain
        {
            Chorus = new ChorusFilter(Rate: 1.5f, Depth: 0.7f, Feedback: 0.4f, Mix: 0.5f),
            Compressor = new CompressorFilter(
                Threshold: -30f,
                Ratio: 6f,
                Attack: 0.02f,
                Release: 0.2f,
                MakeupGain: 2f
            ),
            Flanger = new FlangerFilter(Rate: 0.5f, Depth: 0.8f, Feedback: 0.3f),
            HighPass = new HighPassFilter(Smoothing: 3f, TargetAlpha: 0.5f),
            Phaser = new PhaserFilter(Rate: 2f, Depth: 0.6f, Stages: 4),
            Phonograph = new PhonographFilter(Crackle: 0.3f, Flutter: 0.2f),
            Reverb = new ReverbFilter(RoomSize: 0.7f, Damping: 0.4f, Mix: 0.8f),
            Spatial = new SpatialFilter(X: 0.5f, Y: -0.5f, Z: 1f, Rate: 3f),
        };

        var round = JsonSerializer.Deserialize<FilterChain>(
            JsonSerializer.Serialize(chain, options),
            options
        );
        await Assert.That(round).IsNotNull();
        await Assert.That(round?.Chorus is { Rate: 1.5f, Depth: 0.7f, Feedback: 0.4f }).IsTrue();
        await Assert
            .That(
                round?.Compressor is { Threshold: -30f, Ratio: 6f, Release: 0.2f, MakeupGain: 2f }
            )
            .IsTrue();
        await Assert.That(round?.Flanger is { Rate: 0.5f, Depth: 0.8f }).IsTrue();
        await Assert.That(round?.HighPass is { Smoothing: 3f, TargetAlpha: 0.5f }).IsTrue();
        await Assert.That(round?.Phaser is { Rate: 2f, Stages: 4 }).IsTrue();
        await Assert.That(round?.Phonograph is { Crackle: 0.3f, Flutter: 0.2f }).IsTrue();
        await Assert.That(round?.Reverb is { RoomSize: 0.7f, Damping: 0.4f, Mix: 0.8f }).IsTrue();
        await Assert.That(round?.Spatial is { X: 0.5f, Y: -0.5f, Z: 1f }).IsTrue();

        var faded = new FilterChain
        {
            Echo = new EchoFilter
            {
                DelayMilliseconds = 200,
                Mix = 0.4f,
                Transition = new(null, "exponential"),
            },
        };
        var fadeJson = JsonSerializer.Serialize(faded, options);
        await Assert
            .That(fadeJson)
            .Contains("\"transition\":{\"durationMs\":0,\"curve\":\"exponential\"}");

        var readBack = JsonSerializer.Deserialize<FilterChain>(fadeJson, options);
        await Assert.That(readBack?.Echo?.Transition is { Curve: "exponential" }).IsTrue();

        var quoted = JsonSerializer.Deserialize<FilterChain>(
            """{"echo":{"delay":100,"mix":0.3,"transition":{"durationMs":"2500"}}}""",
            options
        );
        await Assert.That(quoted?.Echo?.Transition is { DurationMilliseconds: 2500 }).IsTrue();

        var rounded = JsonSerializer.Deserialize<FilterChain>(
            """{"echo":{"delay":100,"mix":0.3,"transition":{"durationMs":2.6}}}""",
            options
        );
        await Assert.That(rounded?.Echo?.Transition is { DurationMilliseconds: 3 }).IsTrue();

        var seconds = JsonSerializer.Deserialize<FilterChain>(
            """{"echo":{"delay":100,"mix":0.3,"transition":{"durationMs":"1.4s"}}}""",
            options
        );
        await Assert.That(seconds?.Echo).IsNull();

        var hostileTransition = JsonSerializer.Deserialize<FilterChain>(
            """{"echo":{"delay":1,"mix":0.3,"transition":{"durationMs":"soon"}}}""",
            options
        );
        await Assert.That(hostileTransition?.Echo).IsNull();
    }

    [Test]
    public async Task Metrics()
    {
        var first = new Node(
            new Endpoint("127.0.0.1", 3001, "youshallnotpass"),
            new NodeOptions { Name = "metrics-one" }
        );
        var second = new Node(
            new Endpoint("127.0.0.1", 3002, "youshallnotpass"),
            new NodeOptions { Name = "metrics-two" }
        );

        var stats = Parse<Stats>(
            """{"players":4,"playingPlayers":2,"uptime":1000,"cpu":{"cores":1,"systemLoad":0.1,"lavalinkLoad":0.2},"frameStats":{"sent":10,"nulled":1,"deficit":2,"expected":13}}"""
        )!;

        using var recorder = new GaugeRecorder();

        NyraMetrics.ReportConnection(first, connected: true);
        NyraMetrics.ReportStats(first, stats);
        var reported = recorder.Read("metrics-one");
        using (Assert.Multiple())
        {
            await Assert.That(reported.Count).IsEqualTo(12);
            await Assert.That(reported["players"]).IsEqualTo(4);
            await Assert.That(reported["playing-players"]).IsEqualTo(2);
            await Assert.That(reported["not-playing-players"]).IsEqualTo(2);
            await Assert.That(reported["node-connected"]).IsEqualTo(1);
            await Assert.That(reported["node-uptime"]).IsEqualTo(1000);
            await Assert.That(reported["cpu-system-load"]).IsEqualTo(0.1);
            await Assert.That(reported["cpu-node-load"]).IsEqualTo(0.2);
            await Assert.That(reported["frames-sent"]).IsEqualTo(10);
            await Assert.That(reported["frames-nulled"]).IsEqualTo(1);
            await Assert.That(reported["frames-deficit"]).IsEqualTo(2);
        }

        NyraMetrics.ReportConnection(second, connected: true);
        var silent = recorder.Read("metrics-two");
        await Assert.That(silent.Keys).IsEquivalentTo(["node-connected"]);

        NyraMetrics.ReportConnection(first, connected: false);
        await Assert.That(recorder.Read("metrics-one")["node-connected"]).IsEqualTo(0);
        NyraMetrics.ReportConnection(first, connected: true);

        NyraMetrics.Forget(first);
        await Assert.That(recorder.Read("metrics-one")).IsEmpty();

        var dead = new Node(
            new Endpoint("127.0.0.1", 3001, "youshallnotpass"),
            new NodeOptions { Name = "metrics-one" }
        );
        var deadStats = Parse<Stats>(
            """{"players":9,"playingPlayers":9,"uptime":5,"cpu":{"cores":1,"systemLoad":0.5,"lavalinkLoad":0.5},"frameStats":{"sent":99,"nulled":9,"deficit":9}}"""
        )!;
        NyraMetrics.ReportConnection(dead, connected: false);
        NyraMetrics.ReportStats(dead, deadStats);
        NyraMetrics.ReportConnection(first, connected: true);
        NyraMetrics.ReportStats(first, stats);
        var duplicate = recorder.Read("metrics-one");
        await Assert.That(duplicate["players"]).IsEqualTo(4);
        await Assert.That(duplicate["node-connected"]).IsEqualTo(1);

        NyraMetrics.Forget(first);
        NyraMetrics.Forget(dead);
        NyraMetrics.Forget(second);
    }

    [Test]
    public async Task SurfaceSweep()
    {
        const string trackJson =
            """{"encoded":"QXRsZWFzdA==","title":"ATWA","author":"Kiedis","length":1000}""";
        var line = new LyricsLine
        {
            Text = "a line",
            Time = 1000,
            Duration = 2000,
        };
        await Assert.That(line).IsEqualTo(line with { });
        await Assert.That(line.GetHashCode()).IsEqualTo((line with { }).GetHashCode());
        var data = new LyricsData { Lines = [line], Message = "ok" };
        await Assert.That(data).IsEqualTo(data with { });
        await Assert.That(data.GetHashCode()).IsEqualTo((data with { }).GetHashCode());
        var icy = new IcyMetadata
        {
            Raw = "raw",
            StreamTitle = "t",
            StreamUrl = "u",
        };
        await Assert.That(icy).IsEqualTo(icy with { });
        await Assert.That(icy.GetHashCode()).IsEqualTo((icy with { }).GetHashCode());
        var mix = new MixLayer { Id = "m1", PositionBytes = 500 };
        await Assert.That(mix).IsEqualTo(mix with { });
        var play = new PlayOptions { StartPosition = TimeSpan.FromSeconds(1) };
        await Assert.That(play.GetHashCode()).IsEqualTo((play with { }).GetHashCode());
        var chapter = new Chapter { Title = "t" };
        await Assert.That(chapter.GetHashCode()).IsEqualTo((chapter with { }).GetHashCode());
        var sb = new SponsorBlockSegment { Uuid = "u", Category = "music" };
        await Assert.That(sb).IsEqualTo(sb with { });
        await Assert.That(sb.GetHashCode()).IsEqualTo((sb with { }).GetHashCode());
        var yt = new YouTubeConfigUpdate { Message = "m", WorkersUpdated = 1 };
        await Assert.That(yt).IsEqualTo(yt with { });
        await Assert.That(yt.GetHashCode()).IsEqualTo((yt with { }).GetHashCode());
        var group = new Group { Id = "g" };
        await Assert.That(group.GetHashCode()).IsEqualTo((group with { }).GetHashCode());
        await Assert.That(group).IsEqualTo(group with { });
        var trackLoad = ParseLava<LoadResult>($$$"""{"loadType":"track","data":{{{trackJson}}}}""");
        var playlistLoad = ParseLava<LoadResult>(
            $$$"""{"loadType":"playlist","data":{"info":{"name":"x","selectedTrack":0},"tracks":[{{{trackJson}}}]}}"""
        );
        var searchLoad = ParseLava<LoadResult>(
            $$$"""{"loadType":"search","data":[{{{trackJson}}}]}"""
        );
        var emptyLoad = ParseLava<LoadResult>("""{"loadType":"empty","data":null}""");
        await Assert.That(trackLoad?.LoadType).IsEqualTo(Entities.LoadType.Track);
        await Assert.That(playlistLoad?.LoadType).IsEqualTo(Entities.LoadType.Playlist);
        await Assert.That(searchLoad?.LoadType).IsEqualTo(Entities.LoadType.Search);
        await Assert.That(emptyLoad?.LoadType).IsEqualTo(Entities.LoadType.Empty);
    }
}
