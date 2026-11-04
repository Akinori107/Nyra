using System.Text.Json;
using Nyra.Entities;
using Nyra.EventArgs;

namespace Nyra.Tests;

public sealed class WireFrame
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private static T? Parse<T>(string json, bool isNodeLink = false)
        where T : class => JsonSerializer.Deserialize<T>(json, NyraJson.OptionsFor(isNodeLink));

    [Test]
    public async Task Ops()
    {
        var ready = Parse<NodeReadyEventArgs>(
            """{"op":"ready","resumed":true,"sessionId":"abc123"}"""
        );
        using (Assert.Multiple())
        {
            await Assert.That(ready).IsNotNull();
            await Assert.That(ready!.Resumed).IsTrue();
            await Assert.That(ready?.SessionId).IsEqualTo("abc123");
        }

        var stats = Parse<Stats>(
            """
            {"op":"stats","players":3,"playingPlayers":2,"uptime":9001,
             "memory":{"free":100,"used":200,"allocated":300,"reservable":400},
             "cpu":{"cores":4,"systemLoad":0.25,"lavalinkLoad":0.5},
             "frameStats":{"sent":10,"nulled":1,"deficit":2,"expected":13}}
            """
        );
        using (Assert.Multiple())
        {
            await Assert.That(stats?.Players).IsEqualTo(3);
            await Assert.That(stats?.PlayingPlayers).IsEqualTo(2);
            await Assert.That(stats?.Memory?.Used).IsEqualTo(200);
            await Assert.That(stats?.Cpu?.Cores).IsEqualTo(4);
            await Assert.That(stats?.FrameStats?.Sent).IsEqualTo(10);
            await Assert.That(stats?.FrameStats?.Expected).IsEqualTo(13);
        }

        var update = Parse<PlayerUpdateEventArgs>(
            """{"op":"playerUpdate","guildId":"123","state":{"time":1500,"position":60000,"connected":true,"ping":50}}"""
        );
        using (Assert.Multiple())
        {
            await Assert.That(update?.GuildId).IsEqualTo(123);
            await Assert.That(update?.State.TimeMilliseconds).IsEqualTo(1500);
            await Assert.That(update?.State.PositionMilliseconds).IsEqualTo(60000);
            await Assert.That(update?.State.Connected).IsTrue();
            await Assert.That(update?.State.Ping).IsEqualTo(50);
        }
    }

    [Test]
    public async Task Events()
    {
        var start = Parse<TrackStartedEventArgs>(
            "{\"type\":\"TrackStartEvent\",\"guildId\":\"77\",\"track\":"
                + TrackJson
                + ",\"playingQuality\":2}"
        );
        using (Assert.Multiple())
        {
            await Assert.That(start?.GuildId).IsEqualTo(77);
            await Assert.That(start?.Track?.Info.Title).IsEqualTo("ATWA");
            await Assert.That(start?.PlayingQuality).IsEqualTo(2);
        }

        foreach (
            var (wire, typed) in new[]
            {
                ("finished", TrackEndReason.Finished),
                ("loadFailed", TrackEndReason.LoadFailed),
                ("stopped", TrackEndReason.Stopped),
                ("replaced", TrackEndReason.Replaced),
                ("cleanup", TrackEndReason.Cleanup),
            }
        )
        {
            var end = Parse<TrackFinishedEventArgs>(
                "{\"type\":\"TrackEndEvent\",\"guildId\":\"1\",\"track\":"
                    + TrackJson
                    + $",\"reason\":\"{wire}\"}}"
            );
            await Assert.That(end?.EndReason).IsEqualTo(typed);
        }
        var unknownReason = Parse<TrackFinishedEventArgs>(
            """{"type":"TrackEndEvent","guildId":"1","reason":"martian"}"""
        );
        await Assert.That(unknownReason?.EndReason).IsEqualTo(TrackEndReason.Unknown);

        var exception = Parse<TrackExceptionEventArgs>(
            "{\"type\":\"TrackExceptionEvent\",\"guildId\":\"5\",\"track\":"
                + TrackJson
                + ",\"exception\":{\"message\":\"boom\",\"severity\":\"suspicious\",\"cause\":\"probe\"}}"
        );
        using (Assert.Multiple())
        {
            await Assert.That(exception?.Exception?.Message).IsEqualTo("boom");
            await Assert.That(exception?.Exception?.Severity).IsEqualTo(ErrorSeverity.Suspicious);
            await Assert.That(exception?.Exception?.Cause).IsEqualTo("probe");
        }

        var stuck = Parse<TrackStuckEventArgs>(
            "{\"type\":\"TrackStuckEvent\",\"guildId\":\"6\",\"track\":"
                + TrackJson
                + ",\"thresholdMs\":400,\"reason\":\"threshold\"}"
        );
        await Assert.That(stuck?.ThresholdMilliseconds).IsEqualTo(400);

        var closed = Parse<WebSocketClosedEventArgs>(
            """{"type":"WebSocketClosedEvent","guildId":"8","code":4014,"reason":"Discord WebSocket closed","byRemote":true}"""
        );
        await Assert.That(closed?.Code).IsEqualTo(4014);
        await Assert.That(closed?.ByRemote).IsTrue();

        var volume = Parse<VolumeChangedEventArgs>(
            """{"type":"VolumeChangedEvent","guildId":"9","volume":75}"""
        );
        await Assert.That(volume?.Volume).IsEqualTo(75);

        var pause = Parse<PauseChangedEventArgs>(
            """{"type":"PauseEvent","guildId":"10","paused":true}"""
        );
        await Assert.That(pause?.IsPaused).IsTrue();

        var sought = Parse<TrackSoughtEventArgs>(
            "{\"type\":\"SeekEvent\",\"guildId\":\"11\",\"position\":42000,\"duration\":134000}"
        );
        await Assert.That(sought?.PositionMilliseconds).IsEqualTo(42000);
        await Assert.That(sought?.DeltaMilliseconds).IsEqualTo(134000);

        var mixStarted = Parse<MixStartedEventArgs>(
            "{\"type\":\"MixStartedEvent\",\"guildId\":\"12\",\"mixId\":\"mx1\",\"track\":"
                + TrackJson
                + ",\"volume\":0.5}"
        );
        await Assert.That(mixStarted?.MixId).IsEqualTo("mx1");
        await Assert.That(mixStarted?.Volume).IsEqualTo(0.5f);

        var mixEnded = Parse<MixEndedEventArgs>(
            """{"type":"MixEndedEvent","guildId":"12","mixId":"mx1","reason":"FINISHED"}"""
        );
        await Assert.That(mixEnded?.MixId).IsEqualTo("mx1");
        await Assert.That(mixEnded?.EndReason).IsEqualTo(MixEndReason.Finished);

        var lyricsFound = Parse<LyricsFoundEventArgs>(
            "{\"type\":\"LyricsFoundEvent\",\"guildId\":\"13\",\"lyrics\":{"
                + "\"sourceName\":\"netease\",\"provider\":\"test\",\"text\":\"full text\","
                + "\"lines\":[{\"timestamp\":3000,\"duration\":2000,\"line\":\"a line\"}]}}"
        );
        var firstLine = lyricsFound?.Lyrics?.Lines?[0];
        await Assert.That(firstLine?.Time).IsEqualTo(3000);
        await Assert.That(firstLine?.Text).IsEqualTo("a line");

        var lyricsLine = Parse<LyricsLineEventArgs>(
            "{\"type\":\"LyricsLineEvent\",\"guildId\":\"13\",\"lineIndex\":4,"
                + "\"line\":{\"timestamp\":5000,\"duration\":2000,\"line\":\"next line\"},\"skipped\":false}"
        );
        await Assert.That(lyricsLine?.LineIndex).IsEqualTo(4);
        await Assert.That(lyricsLine?.Skipped).IsFalse();

        var segments = Parse<SponsorBlockSegmentsLoadedEventArgs>(
            """
            {"type":"SponsorBlockSegmentsLoadedEvent","guildId":"14","segments":[
              {"uuid":"u1","start":1000,"end":5000,"category":"sponsor","actionType":"skip","votes":3,"locked":true,"videoDuration":176000}]}
            """
        );
        var segment = segments?.Segments?[0];
        using (Assert.Multiple())
        {
            await Assert.That(segment?.Uuid).IsEqualTo("u1");
            await Assert.That(segment?.Start).IsEqualTo(1000);
            await Assert.That(segment?.End).IsEqualTo(5000);
            await Assert.That(segment?.Locked).IsTrue();
            await Assert.That(segment?.Category).IsEqualTo("sponsor");
        }

        var skipped = Parse<SponsorBlockSegmentSkippedEventArgs>(
            "{\"type\":\"SponsorBlockSegmentSkippedEvent\",\"guildId\":\"14\",\"track\":"
                + TrackJson
                + ",\"segment\":{\"uuid\":\"u1\",\"start\":1000,\"end\":5000,\"category\":\"sponsor\"}}"
        );
        await Assert.That(skipped?.Segment?.Uuid).IsEqualTo("u1");

        var created = Parse<PlayerCreatedEventArgs>(
            """{"type":"PlayerCreatedEvent","guildId":"15","player":{"guildId":15}}"""
        );
        await Assert.That(created?.GuildId).IsEqualTo(15);

        var destroyed = Parse<PlayerDestroyedEventArgs>(
            """{"type":"PlayerDestroyedEvent","guildId":"15"}"""
        );
        await Assert.That(destroyed?.GuildId).IsEqualTo(15);

        var filtersChanged = Parse<FiltersChangedEventArgs>(
            """{"type":"FiltersChangedEvent","guildId":"16","filters":{"volume":0.9}}""",
            isNodeLink: true
        );
        await Assert.That(filtersChanged?.Filters?.Volume).IsEqualTo(0.9f);
    }

    [Test]
    public async Task Outbound()
    {
        var options = NyraJson.OptionsFor(isNodeLink: true);

        var volumeOnly = JsonSerializer.Serialize(new PlayerUpdatePayload { Volume = 50 }, options);
        await Assert.That(volumeOnly).IsEqualTo("""{"volume":50}""");

        var fullUpdate = JsonSerializer.Serialize(
            new PlayerUpdatePayload
            {
                Volume = 80,
                Paused = true,
                Position = 1234,
                EndTime = 5678,
            },
            options
        );
        await Assert
            .That(fullUpdate)
            .IsEqualTo("""{"position":1234,"endTime":5678,"volume":80,"paused":true}""");

        var endTime = JsonSerializer.Serialize(new EndTimeUpdate { EndTime = 90000 }, options);
        await Assert.That(endTime).IsEqualTo("""{"endTime":90000}""");

        var mixVolume = JsonSerializer.Serialize(new MixLayerVolume(0.25f), options);
        await Assert.That(mixVolume).IsEqualTo("""{"volume":0.25}""");

        var freeAddress = JsonSerializer.Serialize(
            new RoutePlannerFreeAddress("10.0.0.1"),
            options
        );
        await Assert.That(freeAddress).IsEqualTo("""{"address":"10.0.0.1"}""");

        var echoed = JsonSerializer.Deserialize<PlayerUpdatePayload>(volumeOnly, options);
        await Assert.That(echoed?.Volume).IsEqualTo(50);
    }

    [Test]
    public async Task Responses()
    {
        var group = Parse<Group>(
            """{"id":"grp1","guildIds":["11","12"],"createdAt":1791976000000}"""
        );
        await Assert.That(group?.Id).IsEqualTo("grp1");
        await Assert.That(group?.GuildIds?.SequenceEqual(new ulong[] { 11, 12 })).IsTrue();

        var balancing = Parse<RoutePlannerStatus>("""{"class":"BalancingIpRoutePlanner"}""");
        await Assert.That(balancing?.Class).IsEqualTo(RoutePlannerClass.Balancing);

        var rotating = Parse<RoutePlannerStatus>(
            """{"class":"RotatingNanoIpRoutePlanner","details":{"failingAddresses":[{"failingAddress":"10.0.0.9","failingTimestamp":1699999999999}]}}"""
        );
        await Assert.That(rotating?.Class).IsEqualTo(RoutePlannerClass.RotatingNano);
        await Assert.That(rotating?.Details?.FailingAddresses?[0]?.Address).IsEqualTo("10.0.0.9");

        var statsEcho = Parse<Stats>(
            """{"players":1,"playingPlayers":1,"uptime":5,"memory":{"free":1,"used":2,"allocated":3,"reservable":4},"cpu":{"cores":8,"systemLoad":0.5,"nodelinkLoad":0.75}}"""
        );
        await Assert.That(statsEcho?.Cpu?.NodelinkLoad).IsEqualTo(0.75);
    }
}
