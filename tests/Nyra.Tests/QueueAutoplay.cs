using System.Collections.Concurrent;
using System.Text.Json;
using Nyra.Entities;
using Nyra.Queue;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class QueueAutoplay
{
    private sealed class Rig : IAsyncDisposable
    {
        public required NyraExtension Extension { get; init; }

        public required FakeNodeServer Server { get; init; }

        public required GuildQueue Queue { get; init; }

        public ConcurrentQueue<string> Log { get; } = [];

        public IEnumerable<string?> Loads =>
            Server
                .RestCalls.Where(call => call.Path == "/v4/loadtracks")
                .Select(call => call.Query);

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private static string TrackJson(string id, string source = "youtube") =>
        $$$"""{"encoded":"enc-{{{id}}}","info":{"identifier":"{{{id}}}","author":"a","title":"{{{id}}}","length":100000,"isStream":false,"isSeekable":true,"position":0,"sourceName":"{{{source}}}"}}""";

    private static Track Make(string id, string source = "youtube") =>
        JsonSerializer.Deserialize<Track>(TrackJson(id, source), NyraJson.SerializerOptions)!;

    private static string Playlist(params string[] ids) =>
        $$$"""{"loadType":"playlist","data":{"info":{"name":"Mix","selectedTrack":-1},"pluginInfo":{},"tracks":[{{{string.Join(",", ids.Select(id => TrackJson(id)))}}}]}}""";

    private static async Task<Rig> StartAsync(
        Func<FakeRestResponse> recommendations,
        bool nodeLink = true,
        bool autoplay = true
    )
    {
        var server = nodeLink
            ? new FakeNodeServer()
            : new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };

        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "autoplay",
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var connection = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

        var current = "null";
        server.On("PATCH", PlayerPath, call =>
        {
            if (call.Body is { } body && body.Contains("\"enc-", StringComparison.Ordinal))
            {
                var start = body.IndexOf("\"enc-", StringComparison.Ordinal) + 5;
                current = TrackJson(body[start..body.IndexOf('"', start)]);
            }

            return new FakeRestResponse(
                200,
                $$$"""{"guildId":"777","track":{{{current}}},"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
            );
        });
        server.On("GET", "/v4/loadtracks", _ => recommendations());

        var queue = connection.UseQueue();
        queue.Autoplay = autoplay;
        var rig = new Rig
        {
            Extension = extension,
            Server = server,
            Queue = queue,
        };
        queue.Advanced += (_, e) =>
        {
            rig.Log.Enqueue("advanced:" + e.Track.Info.Identifier);
            return Task.CompletedTask;
        };
        queue.Ended += (_, _) =>
        {
            rig.Log.Enqueue("ended");
            return Task.CompletedTask;
        };
        return rig;
    }

    private static Task PushEndAsync(Rig rig, string id, string source = "youtube") =>
        rig.Server.PushAsync(
            $$"""{"op":"event","guildId":"777","type":"TrackEndEvent","track":{{TrackJson(id, source)}},"reason":"finished"}"""
        );

    private static async Task Eventually(Func<bool> condition) =>
        await Assert.That(condition).WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

    private static bool Queried(Rig rig, string identifier) =>
        rig.Loads.Any(query =>
            query?.Contains(
                "identifier=" + Uri.EscapeDataString(identifier),
                StringComparison.Ordinal
            ) == true
        );

    [Test]
    public async Task PlaysTheFirstRecommendationThatIsNotTheTrackItself()
    {
        var rig = await StartAsync(() => new FakeRestResponse(200, Playlist("a", "b", "c")))
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Queue.EnqueueAsync(Make("a")).ConfigureAwait(false);
            await PushEndAsync(rig, "a").ConfigureAwait(false);

            await Eventually(() => rig.Queue.Current?.Info.Identifier == "b").ConfigureAwait(false);
            await Assert.That(Queried(rig, "ytrec:a")).IsTrue();
            await Assert.That(rig.Log.Contains("advanced:b")).IsTrue();
            await Assert.That(rig.Log.Contains("ended")).IsFalse();
        }
    }

    [Test]
    public async Task SkipsTracksAlreadyInTheHistory()
    {
        var rig = await StartAsync(() => new FakeRestResponse(200, Playlist("a", "b", "c")))
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Queue.EnqueueAsync([Make("a"), Make("b")]).ConfigureAwait(false);
            await PushEndAsync(rig, "a").ConfigureAwait(false);
            await Eventually(() => rig.Queue.Current?.Info.Identifier == "b").ConfigureAwait(false);
            await PushEndAsync(rig, "b").ConfigureAwait(false);

            await Eventually(() => rig.Queue.Current?.Info.Identifier == "c").ConfigureAwait(false);
            await Assert.That(Queried(rig, "ytrec:b")).IsTrue();
        }
    }

    [Test]
    public async Task LavalinkUsesTheYouTubeMix()
    {
        var rig = await StartAsync(
                () => new FakeRestResponse(200, Playlist("a", "b")),
                nodeLink: false
            )
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Queue.EnqueueAsync(Make("a")).ConfigureAwait(false);
            await PushEndAsync(rig, "a").ConfigureAwait(false);

            await Eventually(() => rig.Queue.Current?.Info.Identifier == "b").ConfigureAwait(false);
            await Assert.That(Queried(rig, "https://www.youtube.com/watch?v=a&list=RDa")).IsTrue();
        }
    }

    [Test]
    public async Task SpotifyTracksUseSprec()
    {
        var rig = await StartAsync(() => new FakeRestResponse(200, Playlist("b")))
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Queue.EnqueueAsync(Make("a", "spotify")).ConfigureAwait(false);
            await PushEndAsync(rig, "a", "spotify").ConfigureAwait(false);

            await Eventually(() => rig.Queue.Current?.Info.Identifier == "b").ConfigureAwait(false);
            await Assert.That(Queried(rig, "sprec:seed_tracks=a")).IsTrue();
        }
    }

    [Test]
    public async Task EndsWhenEveryRecommendationRepeats()
    {
        var rig = await StartAsync(() => new FakeRestResponse(200, Playlist("a")))
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Queue.EnqueueAsync(Make("a")).ConfigureAwait(false);
            await PushEndAsync(rig, "a").ConfigureAwait(false);

            await Eventually(() => rig.Log.Contains("ended")).ConfigureAwait(false);
            await Assert.That(rig.Queue.Current).IsNull();
        }
    }

    [Test]
    public async Task EndsWhenTheLoadFails()
    {
        var rig = await StartAsync(() => new FakeRestResponse(500, """{"message":"boom"}"""))
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Queue.EnqueueAsync(Make("a")).ConfigureAwait(false);
            await PushEndAsync(rig, "a").ConfigureAwait(false);

            await Eventually(() => rig.Log.Contains("ended")).ConfigureAwait(false);
            await Assert.That(rig.Queue.Current).IsNull();
        }
    }

    [Test]
    public async Task ASourceWithoutRecommendationsEndsWithoutLoading()
    {
        var rig = await StartAsync(() => new FakeRestResponse(200, Playlist("b")))
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Queue.EnqueueAsync(Make("a", "soundcloud")).ConfigureAwait(false);
            await PushEndAsync(rig, "a", "soundcloud").ConfigureAwait(false);

            await Eventually(() => rig.Log.Contains("ended")).ConfigureAwait(false);
            await Assert.That(rig.Loads).IsEmpty();
        }
    }

    [Test]
    public async Task OffByDefault()
    {
        var rig = await StartAsync(() => new FakeRestResponse(200, Playlist("b")), autoplay: false)
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Queue.EnqueueAsync(Make("a")).ConfigureAwait(false);
            await PushEndAsync(rig, "a").ConfigureAwait(false);

            await Eventually(() => rig.Log.Contains("ended")).ConfigureAwait(false);
            await Assert.That(rig.Loads).IsEmpty();
        }
    }

    [Test]
    public async Task ALoopingQueueNeverReachesAutoplay()
    {
        var rig = await StartAsync(() => new FakeRestResponse(200, Playlist("b")))
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Queue.Loop = LoopMode.Queue;
            await rig.Queue.EnqueueAsync(Make("a")).ConfigureAwait(false);
            await PushEndAsync(rig, "a").ConfigureAwait(false);

            await Eventually(() => rig.Server.RestCalls.Count(call => call.Method == "PATCH") >= 2)
                .ConfigureAwait(false);
            await Assert.That(rig.Queue.Current?.Info.Identifier).IsEqualTo("a");
            await Assert.That(rig.Loads).IsEmpty();
        }
    }

    [Test]
    [Arguments("search")]
    [Arguments("track")]
    public async Task PlaysARecommendationFromAnyResultKind(string kind)
    {
        var data = kind == "search" ? $"[{TrackJson("b")}]" : TrackJson("b");
        var rig = await StartAsync(() =>
                new FakeRestResponse(200, $$"""{"loadType":"{{kind}}","data":{{data}}}""")
            )
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Queue.EnqueueAsync(Make("a")).ConfigureAwait(false);
            await PushEndAsync(rig, "a").ConfigureAwait(false);

            await Eventually(() => rig.Queue.Current?.Info.Identifier == "b").ConfigureAwait(false);
            await Assert.That(rig.Log.Contains("ended")).IsFalse();
        }
    }

    [Test]
    public async Task EndsWhenTheResultHasNoTracks()
    {
        var rig = await StartAsync(() =>
                new FakeRestResponse(200, """{"loadType":"empty","data":{}}""")
            )
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Queue.EnqueueAsync(Make("a")).ConfigureAwait(false);
            await PushEndAsync(rig, "a").ConfigureAwait(false);

            await Eventually(() => rig.Log.Contains("ended")).ConfigureAwait(false);
            await Assert.That(Queried(rig, "ytrec:a")).IsTrue();
        }
    }

    [Test]
    public async Task ATrackWithoutAnIdentifierEndsWithoutLoading()
    {
        var rig = await StartAsync(() => new FakeRestResponse(200, Playlist("b")))
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Queue.EnqueueAsync(Make("")).ConfigureAwait(false);
            await PushEndAsync(rig, "").ConfigureAwait(false);

            await Eventually(() => rig.Log.Contains("ended")).ConfigureAwait(false);
            await Assert.That(rig.Loads).IsEmpty();
        }
    }
}
