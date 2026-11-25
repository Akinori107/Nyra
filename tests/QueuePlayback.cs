using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Nyra.Entities;
using Nyra.Queue;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class QueuePlayback
{
    private sealed class Rig : IAsyncDisposable
    {
        public required NyraExtension Extension { get; init; }

        public required FakeNodeServer Server { get; init; }

        public required Node Node { get; init; }

        public required GuildQueue Queue { get; init; }

        public ConcurrentQueue<string> Log { get; } = [];

        public string Played =>
            string.Join(
                ",",
                Server
                    .RestCalls.Where(call => call.Method == "PATCH")
                    .Select(call => PlayedId(call.Body))
                    .OfType<string>()
            );

        public string? CurrentId => Queue.Current?.Info.Identifier;

        public async Task<Track> MakeAsync(string id, long length = 100000, bool stream = false)
        {
            var loaded = await Node.LoadTracksAsync($"{id}|{length}|{stream}")
                .ConfigureAwait(false);
            return ((TrackLoaded)loaded).Track;
        }

        public async Task<Track?> EnqueueAsync(params string[] ids)
        {
            var tracks = new List<Track>();
            foreach (var id in ids)
                tracks.Add(await MakeAsync(id).ConfigureAwait(false));
            return await Queue.EnqueueAsync(tracks).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private static string? PlayedId(string? body)
    {
        if (body is null)
            return null;

        using var document = JsonDocument.Parse(body);
        return
            document.RootElement.TryGetProperty("track", out var track)
            && track.ValueKind == JsonValueKind.Object
            && track.TryGetProperty("encoded", out var encoded)
            && encoded.GetString() is { } value
            && value.StartsWith("enc-", StringComparison.Ordinal)
            ? value[4..]
            : null;
    }

    private static string TrackJson(string id, long length = 100000, bool stream = false) =>
        $$$"""{"encoded":"enc-{{{id}}}","info":{"identifier":"{{{id}}}","author":"a","title":"{{{id}}}","length":{{{length}}},"isStream":{{{(stream ? "true" : "false")}}},"isSeekable":true,"position":0}}""";

    private static string LoadedTrackJson(string? query)
    {
        var identifier = Uri.UnescapeDataString(
            (query ?? "")
                .TrimStart('?')
                .Split('&')
                .First(part => part.StartsWith("identifier=", StringComparison.Ordinal))[
                "identifier=".Length..
            ]
        );
        var parts = identifier[(identifier.IndexOf(':', StringComparison.Ordinal) + 1)..]
            .Split('|');
        return TrackJson(
            parts[0],
            long.Parse(parts[1], CultureInfo.InvariantCulture),
            bool.Parse(parts[2])
        );
    }

    private static async Task<Rig> StartAsync()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "queue",
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var connection = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

        server.On("GET", "/v4/loadtracks", call => new FakeRestResponse(
            200,
            $$"""{"loadType":"track","data":{{LoadedTrackJson(call.Query)}}}"""
        ));
        var current = "null";
        server.On("PATCH", PlayerPath, call =>
        {
            if (PlayedId(call.Body) is { } played)
                current = TrackJson(played);
            return new FakeRestResponse(
                200,
                $$$"""{"guildId":"777","track":{{{current}}},"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
            );
        });

        var queue = connection.UseQueue();
        var rig = new Rig
        {
            Extension = extension,
            Server = server,
            Node = node,
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
        queue.TrackFailed += (_, e) =>
        {
            rig.Log.Enqueue("failed:" + e.Track.Info.Identifier);
            return Task.CompletedTask;
        };
        return rig;
    }

    private static async Task Eventually(Func<bool> condition) =>
        await Assert.That(condition).WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

    private static Task PushEndAsync(Rig rig, string id, string reason) =>
        rig.Server.PushAsync(
            $$"""{"op":"event","guildId":"777","type":"TrackEndEvent","track":{{TrackJson(id)}},"reason":"{{reason}}"}"""
        );

    private static Task PushStuckAsync(Rig rig, string id) =>
        rig.Server.PushAsync(
            $$$"""{"op":"event","guildId":"777","type":"TrackStuckEvent","track":{{{TrackJson(id)}}},"thresholdMs":300}"""
        );

    private static string Ids(IEnumerable<Track> tracks) =>
        string.Join(",", tracks.Select(track => track.Info.Identifier));

    [Test]
    public async Task EnqueueOnAnIdleQueueStartsTheTrack()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var started = await rig.EnqueueAsync("a").ConfigureAwait(false);

            await Assert.That(started?.Info.Identifier).IsEqualTo("a");
            await Assert.That(rig.CurrentId).IsEqualTo("a");
            await Assert.That(rig.Played).IsEqualTo("a");
            await Eventually(() => rig.Log.Contains("advanced:a")).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task EnqueueWhilePlayingOnlyQueues()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a").ConfigureAwait(false);
            var started = await rig.EnqueueAsync("b").ConfigureAwait(false);

            await Assert.That(started).IsNull();
            await Assert.That(Ids(rig.Queue.Upcoming)).IsEqualTo("b");
            await Assert.That(rig.Played).IsEqualTo("a");
        }
    }

    [Test]
    public async Task SkipPlaysTheNextTrack()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b", "c").ConfigureAwait(false);
            var skipped = await rig.Queue.SkipAsync().ConfigureAwait(false);

            await Assert.That(skipped?.Info.Identifier).IsEqualTo("b");
            await Assert.That(Ids(rig.Queue.History)).IsEqualTo("a");
            await Assert.That(Ids(rig.Queue.Upcoming)).IsEqualTo("c");
            await Assert.That(rig.Played).IsEqualTo("a,b");
        }
    }

    [Test]
    public async Task FinishedTrackAdvancesToTheNext()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await PushEndAsync(rig, "a", "finished").ConfigureAwait(false);

            await Eventually(() => rig.Log.Contains("advanced:b")).ConfigureAwait(false);
            await Assert.That(rig.CurrentId).IsEqualTo("b");
            await Assert.That(rig.Played).IsEqualTo("a,b");
        }
    }

    [Test]
    public async Task TheLastTrackFinishingRaisesEnded()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a").ConfigureAwait(false);
            await PushEndAsync(rig, "a", "finished").ConfigureAwait(false);

            await Eventually(() => rig.Log.Contains("ended")).ConfigureAwait(false);
            await Assert.That(rig.Queue.Current).IsNull();
            await Assert.That(Ids(rig.Queue.History)).IsEqualTo("a");
        }
    }

    [Test]
    public async Task MaxLengthRejectsTheWholeEnqueue()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Queue.MaxLength = 2;
            await rig.EnqueueAsync("a").ConfigureAwait(false);

            await Assert
                .That(async () => await rig.EnqueueAsync("b", "c", "d").ConfigureAwait(false))
                .Throws<InvalidOperationException>();
            await Assert.That(rig.Queue.Upcoming.Count).IsEqualTo(0);

            await rig.EnqueueAsync("b", "c").ConfigureAwait(false);
            await Assert.That(Ids(rig.Queue.Upcoming)).IsEqualTo("b,c");
        }
    }

    [Test]
    public async Task TotalDurationAddsTheUpcomingTracksAndSkipsStreams()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig
                .Queue.EnqueueAsync([
                    await rig.MakeAsync("a").ConfigureAwait(false),
                    await rig.MakeAsync("b", 60000).ConfigureAwait(false),
                    await rig.MakeAsync("c", 40000).ConfigureAwait(false),
                    await rig.MakeAsync("s", 500000, stream: true).ConfigureAwait(false),
                ])
                .ConfigureAwait(false);

            await Assert.That(rig.Queue.TotalDuration).IsEqualTo(TimeSpan.FromSeconds(100));
        }
    }

    [Test]
    public async Task AStuckTrackRaisesTrackFailedAndSkipsIt()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await PushStuckAsync(rig, "a").ConfigureAwait(false);

            await Eventually(() => rig.Log.Contains("advanced:b")).ConfigureAwait(false);
            string[] log = [.. rig.Log];
            await Assert.That(Array.IndexOf(log, "failed:a")).IsGreaterThanOrEqualTo(0);
            await Assert
                .That(Array.IndexOf(log, "failed:a"))
                .IsLessThan(Array.IndexOf(log, "advanced:b"));
            await Assert.That(rig.Played).IsEqualTo("a,b");
        }
    }

    [Test]
    public async Task StopClearsTheQueueAndTheLoop()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            rig.Queue.Loop = LoopMode.Queue;
            await rig.Queue.StopAsync().ConfigureAwait(false);

            await Assert.That(rig.Queue.Current).IsNull();
            await Assert.That(rig.Queue.Upcoming.Count).IsEqualTo(0);
            await Assert.That(rig.Queue.Loop).IsEqualTo(LoopMode.Off);
        }
    }

    [Test]
    public async Task PreviousPlaysTheLastTrackAgain()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await rig.Queue.SkipAsync().ConfigureAwait(false);
            var previous = await rig.Queue.PreviousAsync().ConfigureAwait(false);

            await Assert.That(previous?.Info.Identifier).IsEqualTo("a");
            await Assert.That(Ids(rig.Queue.Upcoming)).IsEqualTo("b");
            await Assert.That(rig.Queue.History.Count).IsEqualTo(0);
            await Assert.That(rig.Played).IsEqualTo("a,b,a");
        }
    }

    [Test]
    public async Task MoveReordersTheUpcomingTracks()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b", "c", "d").ConfigureAwait(false);

            await Assert.That(await rig.Queue.MoveAsync(2, 0).ConfigureAwait(false)).IsTrue();
            await Assert.That(await rig.Queue.MoveAsync(0, 9).ConfigureAwait(false)).IsFalse();
            await Assert.That(Ids(rig.Queue.Upcoming)).IsEqualTo("d,b,c");
        }
    }

    [Test]
    public async Task ShuffleKeepsTheSameTracks()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b", "c", "d", "e").ConfigureAwait(false);
            await rig.Queue.ShuffleAsync().ConfigureAwait(false);

            await Assert.That(rig.CurrentId).IsEqualTo("a");
            await Assert
                .That(Ids(rig.Queue.Upcoming.OrderBy(track => track.Info.Identifier)))
                .IsEqualTo("b,c,d,e");
        }
    }
}
