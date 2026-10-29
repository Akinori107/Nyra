using System.Diagnostics;
using System.Text.Json;
using Nyra.Entities;
using Nyra.Internal;

namespace Nyra.Tests;

public sealed class EntityAndOptionsSurface
{
    private static readonly JsonSerializerOptions Options = NyraJson.SerializerOptions;

    private static T? Parse<T>(string json)
        where T : class => JsonSerializer.Deserialize<T>(json, Options);

    private static T? Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
            return null;
        }
        catch (T exception)
        {
            return exception;
        }
    }

    private static readonly string TrackBody =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"A","title":"T","length":1000,"isStream":false,"isSeekable":true,"position":0}}""";

    [Test]
    public async Task Endpoint()
    {
        await Assert.That(new Endpoint("[::]", 3000, "pass").Host).IsEqualTo("[::]");
        await Assert.That(new Endpoint("[zzz]", 3000, "pass").Host).IsEqualTo("[zzz]");
    }

    [Test]
    public async Task VersionCompare()
    {
        NodeVersion.TryParse("2.0.0", out var two);
        NodeVersion.TryParse("1.9.9", out var older);
        NodeVersion.TryParse("2.1.0", out var newerMinor);
        NodeVersion.TryParse("2.0.1", out var newerPatch);
        using (Assert.Multiple())
        {
            await Assert.That(two.CompareTo(older)).IsGreaterThan(0);
            await Assert.That(two.CompareTo(newerMinor)).IsLessThan(0);
            await Assert.That(two.CompareTo(newerPatch)).IsLessThan(0);
            await Assert.That(two.CompareTo(two)).IsEqualTo(0);
        }
    }

    [Test]
    public async Task FaultObservation()
    {
        Exception? observed = null;
        var source = new TaskCompletionSource();
        source.SetException(new InvalidOperationException("observed fault"));
        source.Task.ObserveFaults(fault => observed = fault);
        var watch = Stopwatch.StartNew();
        while (observed is null && watch.ElapsedMilliseconds < 2000)
            await Task.Delay(10).ConfigureAwait(false);
        using (Assert.Multiple())
        {
            await Assert.That(observed).IsTypeOf<AggregateException>();
            await Assert.That(observed!.InnerException).IsTypeOf<InvalidOperationException>();
            await Assert.That(observed.InnerException!.Message).IsEqualTo("observed fault");
        }

        var bare = new TaskCompletionSource();
        bare.SetException(new InvalidOperationException("bare fault"));
        await Assert.That(() => bare.Task.ObserveFaults()).ThrowsNothing();
        await Assert.That(bare.Task.IsFaulted).IsTrue();
    }

    [Test]
    public async Task UrlTimecode()
    {
        using (Assert.Multiple())
        {
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://youtu.be/x?t=1h30m"))
                .IsEqualTo(5_400_000);
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://youtu.be/x?start=90"))
                .IsEqualTo(90_000);
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://youtu.be/x?t=00:01:30"))
                .IsEqualTo(90_000);
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://youtu.be/x?t=10:20"))
                .IsEqualTo(620_000);
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://youtu.be/x?foo=1&t=2m"))
                .IsEqualTo(120_000);
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://youtu.be/x?t=5x"))
                .IsNull();
            await Assert
                .That(UrlStartPosition.ParseMilliseconds("https://youtu.be/x?t=999999999999"))
                .IsNull();
            await Assert.That(UrlStartPosition.ParseMilliseconds("https://youtu.be/x?t=")).IsNull();
            await Assert.That(UrlStartPosition.ParseMilliseconds("https://youtu.be/x")).IsNull();
        }
    }

    [Test]
    public async Task Guard()
    {
        using var lavalink = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", lavalink.Port, lavalink.Password),
            new NodeOptions
            {
                Name = "guardd",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await node.EnsureServerInfoAsync(default, forceProbe: true).ConfigureAwait(false);

        await Assert
            .That(() => NodeGuards.RequireNodeLinkOrPlugin(node, "sponsorblock", "SponsorBlock"))
            .Throws<NotSupportedException>();

        using var nodelink = new FakeNodeServer();
        var linked = new Node(
            new Endpoint("127.0.0.1", nodelink.Port, nodelink.Password),
            new NodeOptions
            {
                Name = "guardn",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(linked);
        await linked.EnsureServerInfoAsync(default, forceProbe: true).ConfigureAwait(false);

        await Assert
            .That(() =>
            {
                NodeGuards.RequireNodeLinkOrPlugin(linked, "whatever", "Feature");
            })
            .ThrowsNothing();

        var stranger = new Node(
            new Endpoint("127.0.0.1", 1, nodelink.Password),
            new NodeOptions { Name = "guards" }
        );
        await Assert
            .That(() =>
            {
                NodeGuards.RequireNodeLinkOrPlugin(stranger, "whatever", "Feature");
            })
            .ThrowsNothing();
    }

    [Test]
    public async Task HashCodes()
    {
        var playlistJson =
            """{"loadType":"playlist","data":{"info":{"name":"n","selectedTrack":-1},"tracks":[]}}""";
        var playlistA = JsonSerializer.Deserialize<LoadResult>(
            playlistJson,
            NyraJson.OptionsFor(false)
        );
        var playlistB = JsonSerializer.Deserialize<LoadResult>(
            playlistJson,
            NyraJson.OptionsFor(false)
        );
        await Assert.That(playlistA?.GetHashCode()).IsEqualTo(playlistB?.GetHashCode());

        var searchJson = """{"loadType":"search","data":[]}""";
        var searchA = JsonSerializer.Deserialize<LoadResult>(
            searchJson,
            NyraJson.OptionsFor(false)
        );
        var searchB = JsonSerializer.Deserialize<LoadResult>(
            searchJson,
            NyraJson.OptionsFor(false)
        );
        await Assert.That(searchA?.GetHashCode()).IsEqualTo(searchB?.GetHashCode());

        var membershipJson = """{"add":[1,2],"remove":[3]}""";
        var membershipA = Parse<GroupMembership>(membershipJson);
        var membershipB = Parse<GroupMembership>(membershipJson);
        await Assert.That(membershipA?.GetHashCode()).IsEqualTo(membershipB?.GetHashCode());
    }

    [Test]
    public async Task Equality()
    {
        var optionsA = new PlayOptions(TimeSpan.FromSeconds(1));
        var optionsB = new PlayOptions(TimeSpan.FromSeconds(1));
        var optionsC = new PlayOptions(TimeSpan.FromSeconds(2));
        using (Assert.Multiple())
        {
            await Assert.That(optionsA).IsEqualTo(optionsB);
            await Assert.That(optionsA).IsNotEqualTo(optionsC);
            await Assert.That(optionsA.Equals(null)).IsFalse();
        }

        var endTimeA = new PlayOptions(EndTime: TimeSpan.FromSeconds(9));
        var endTimeB = new PlayOptions(EndTime: TimeSpan.FromSeconds(8));
        await Assert.That(endTimeA).IsNotEqualTo(endTimeB);

        var replaceA = new PlayOptions(NoReplace: true);
        var replaceB = new PlayOptions(NoReplace: false);
        await Assert.That(replaceA).IsNotEqualTo(replaceB);

        var dubA = new PlayOptions(AudioTrackId: "dub1");
        var dubB = new PlayOptions(AudioTrackId: null);
        await Assert.That(dubA).IsNotEqualTo(dubB);

        var docA = JsonDocument.Parse("""{"k":1}""");
        var docB = JsonDocument.Parse("""{"k":1}""");
        var docC = JsonDocument.Parse("""{"k":2}""");
        var userA = new PlayOptions(UserData: docA.RootElement);
        var userB = new PlayOptions(UserData: docB.RootElement);
        var userC = new PlayOptions(UserData: docC.RootElement);
        await Assert.That(userA).IsEqualTo(userB);
        await Assert.That(userA).IsNotEqualTo(userC);

        var settingsA = new SponsorBlockSettings
        {
            Categories = new List<string> { "sponsor" },
            SkipMarginMilliseconds = 100,
        };
        var settingsB = new SponsorBlockSettings
        {
            Categories = new List<string> { "sponsor" },
            SkipMarginMilliseconds = 100,
        };
        var settingsC = new SponsorBlockSettings
        {
            Categories = new List<string> { "selfpromo" },
            SkipMarginMilliseconds = 100,
        };
        using (Assert.Multiple())
        {
            await Assert.That(settingsA).IsEqualTo(settingsB);
            await Assert.That(settingsA).IsNotEqualTo(settingsC);
            await Assert.That(settingsA.Equals(null)).IsFalse();
        }

        var configA = Parse<YouTubeConfigUpdate>("""{"fieldsUpdated":["a"]}""");
        var configB = Parse<YouTubeConfigUpdate>("""{"fieldsUpdated":["a"]}""");
        var configC = Parse<YouTubeConfigUpdate>("""{"fieldsUpdated":["b"]}""");
        using (Assert.Multiple())
        {
            await Assert.That(configA).IsEqualTo(configB);
            await Assert.That(configA!.GetHashCode()).IsEqualTo(configB!.GetHashCode());
            await Assert.That(configA?.Equals(configC)).IsFalse();
        }

        var chatty = Parse<YouTubeConfigUpdate>("""{"message":"done","fieldsUpdated":["a"]}""");
        var quiet = Parse<YouTubeConfigUpdate>("""{"fieldsUpdated":["a"]}""");
        await Assert.That(chatty).IsNotEqualTo(quiet);

        var oneWorker = Parse<YouTubeConfigUpdate>(
            """{"workersUpdated":1,"fieldsUpdated":["a"]}"""
        );
        await Assert.That(oneWorker).IsNotEqualTo(quiet);
    }

    [Test]
    public async Task FadingString()
    {
        var stringFade = Parse<FadeSection>("""{"duration":"1500"}""");
        await Assert.That(stringFade?.DurationMilliseconds).IsEqualTo(1500);

        var huge = Parse<FadeSection>("""{"duration":"1e300"}""");
        await Assert.That(huge?.DurationMilliseconds).IsEqualTo(int.MaxValue);

        var tiny = Parse<FadeSection>("""{"duration":"-1e300"}""");
        await Assert.That(tiny?.DurationMilliseconds).IsEqualTo(int.MinValue);

        var numeric = Parse<FadeSection>("""{"duration":2500}""");
        await Assert.That(numeric?.DurationMilliseconds).IsEqualTo(2500);

        var garbageFade = Throws<JsonException>(() => Parse<FadeSection>("""{"duration":"abc"}"""));
    }

    [Test]
    public async Task GroupUserDataMember()
    {
        var trackUpdate = Parse<GroupUpdate>("""{"track":{"userData":{"k":1}}}""");
        await Assert
            .That(
                trackUpdate?.Track?.UserData is { ValueKind: JsonValueKind.Object } data
                    && data.GetProperty("k").GetInt32() == 1
            )
            .IsTrue();

        var dubbed = Parse<GroupUpdate>("""{"track":{"audioTrackId":"dub1","language":"pt"}}""");
        await Assert.That(dubbed?.Track?.AudioTrackId).IsEqualTo("dub1");

        var silentDub = Parse<GroupUpdate>("""{"track":{"audioTrackId":5}}""");
        await Assert.That(string.IsNullOrEmpty(silentDub?.Track?.AudioTrackId)).IsTrue();
    }

    [Test]
    public async Task AudioTracks()
    {
        var withTracks = Parse<Track>(
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"A","title":"T","length":1000,"isStream":false,"isSeekable":true,"position":0},"pluginInfo":{"audioTracks":"[{\"id\":\"v1\",\"name\":\"dub\"},{\"id\":null}]"}}"""
        );
        var audio = withTracks?.GetYouTubeAudioTracks();
        await Assert.That(audio).IsNotNull();
        await Assert.That(audio!.Count).IsEqualTo(1);
        await Assert.That(audio![0].Id).IsEqualTo("v1");

        var idless = Parse<Track>(
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"A","title":"T","length":1000,"isStream":false,"isSeekable":true,"position":0},"pluginInfo":{"audioTracks":"[{\"name\":\"n\"}]"}}"""
        );
        var kept = idless?.GetYouTubeAudioTracks();
        await Assert.That(kept).IsNotNull();
        await Assert.That(kept!.Count).IsEqualTo(1);
        await Assert.That(kept![0].Id).IsEqualTo(string.Empty);

        var allDropped = Parse<Track>(
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"A","title":"T","length":1000,"isStream":false,"isSeekable":true,"position":0},"pluginInfo":{"audioTracks":"[{\"id\":null}]"}}"""
        );
        await Assert.That(allDropped?.GetYouTubeAudioTracks() is { Count: 0 }).IsTrue();

        var garbage = Parse<Track>(
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"A","title":"T","length":1000,"isStream":false,"isSeekable":true,"position":0},"pluginInfo":{"audioTracks":"not json"}}"""
        );
        await Assert.That(garbage?.GetYouTubeAudioTracks()).IsNull();

        var numeric = Parse<Track>(
            """{"encoded":"QXRsZWFzdA==","info":{"identifier":"i","author":"A","title":"T","length":1000,"isStream":false,"isSeekable":true,"position":0},"pluginInfo":{"audioTracks":5}}"""
        );
        await Assert.That(numeric?.GetYouTubeAudioTracks()).IsNull();

        var bare = Parse<Track>(TrackBody);
        await Assert.That(bare?.GetYouTubeAudioTracks()).IsNull();
    }
}
