using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Nyra.Entities;
using Nyra.Exceptions;
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

        public required GuildConnection Connection { get; init; }

        public required GuildQueue Queue { get; init; }

        public required StrongBox<bool> FailPlays { get; init; }

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

        public Task<Track?> EnqueueAsync(params string[] ids) =>
            Queue.EnqueueAsync(ids.Select(id => Make(id)));

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private static string? PlayedId(string? body) => EncodedId(body, "track");

    private static string? PreloadedId(string? body) => EncodedId(body, "nextTrack");

    private static string? EncodedId(string? body, string member)
    {
        if (body is null)
            return null;

        using var document = JsonDocument.Parse(body);
        return
            document.RootElement.TryGetProperty(member, out var track)
            && track.ValueKind == JsonValueKind.Object
            && track.TryGetProperty("encoded", out var encoded)
            && encoded.GetString() is { } value
            && value.StartsWith("enc-", StringComparison.Ordinal)
            ? value[4..]
            : null;
    }

    private static string TrackJson(string id, long length = 100000, bool stream = false) =>
        $$$"""{"encoded":"enc-{{{id}}}","info":{"identifier":"{{{id}}}","author":"a","title":"{{{id}}}","length":{{{length}}},"isStream":{{{(stream ? "true" : "false")}}},"isSeekable":true,"position":0}}""";

    private static Track Make(string id, long length = 100000, bool stream = false) =>
        JsonSerializer.Deserialize<Track>(
            TrackJson(id, length, stream),
            NyraJson.SerializerOptions
        )!;

    private static async Task<Rig> StartAsync(
        bool resume = true,
        bool rescue = false,
        TimeProvider? time = null
    )
    {
        var server = new FakeNodeServer { ResumeOnReconnect = resume };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "queue",
                TimeProvider = time ?? TimeProvider.System,
                RescuePlayback = rescue,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var connection = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

        var current = "null";
        var failPlays = new StrongBox<bool>();
        server.On("PATCH", PlayerPath, call =>
        {
            if (failPlays.Value)
                return new FakeRestResponse(500, "{\"message\":\"boom\"}");

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
            Connection = connection,
            Queue = queue,
            FailPlays = failPlays,
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
        queue.NextChanged += (_, e) =>
        {
            rig.Log.Enqueue("next:" + (e.Next?.Info.Identifier ?? "none"));
            return Task.CompletedTask;
        };
        queue.HandlerError += (_, e) =>
        {
            rig.Log.Enqueue("error:" + e.Exception.Message);
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

    private static Task PushStartAsync(Rig rig, string id) =>
        rig.Server.PushAsync(
            $$"""{"op":"event","guildId":"777","type":"TrackStartEvent","track":{{TrackJson(id)}}}"""
        );

    private static string Ids(IEnumerable<Track> tracks) =>
        string.Join(",", tracks.Select(track => track.Info.Identifier));

    [Test]
    public async Task UseQueueReturnsTheSameQueueEveryTime()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await Assert.That(rig.Connection.GetQueue()).IsSameReferenceAs(rig.Queue);
            await Assert.That(rig.Connection.UseQueue()).IsSameReferenceAs(rig.Queue);
        }
    }

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
            await Assert.That(rig.Log.Contains("advanced:a")).IsTrue();
        }
    }

    [Test]
    public async Task ThePreloadedNextTrackGoesInTheSamePlayUpdate()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Queue.PreloadNext = true;
            var before = rig.Server.RestCalls.Count;

            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await rig
                .Server.PushAsync(
                    $$"""{"op":"event","guildId":"777","type":"TrackStartEvent","track":{{TrackJson("a")}}}"""
                )
                .ConfigureAwait(false);
            await Eventually(() => rig.Connection.CurrentTrack is not null).ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);

            var patches = rig
                .Server.RestCalls.Skip(before)
                .Where(call => call.Method == "PATCH")
                .Select(call => call.Body ?? string.Empty)
                .ToList();
            await Assert.That(patches.Count).IsEqualTo(1);
            await Assert.That(PlayedId(patches[0])).IsEqualTo("a");
            await Assert.That(patches[0]).Contains("\"nextTrack\":{\"encoded\":\"enc-b\"");
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
    public async Task SkipToAPositionKeepsTheTracksBefore()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b", "c", "d").ConfigureAwait(false);
            var skipped = await rig.Queue.SkipAsync(2).ConfigureAwait(false);

            await Assert.That(skipped?.Info.Identifier).IsEqualTo("c");
            await Assert.That(Ids(rig.Queue.History)).IsEqualTo("a");
            await Assert.That(Ids(rig.Queue.Upcoming)).IsEqualTo("b,d");
            await Assert.That(rig.Played).IsEqualTo("a,c");
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
    public async Task TheEndOfAnotherTrackIsIgnored()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await PushEndAsync(rig, "stranger", "finished").ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);

            await Assert.That(rig.CurrentId).IsEqualTo("a");
            await Assert.That(rig.Played).IsEqualTo("a");
        }
    }

    [Test]
    public async Task AReplacedTrackDoesNotAdvanceTheQueue()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await PushEndAsync(rig, "a", "replaced").ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);

            await Assert.That(rig.CurrentId).IsEqualTo("a");
            await Assert.That(rig.Played).IsEqualTo("a");
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
    public async Task MaxLengthRejectsAValueBelowOne()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
            await Assert.That(() => rig.Queue.MaxLength = 0).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task RemoveAllRemovesTheMatchingTracks()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b", "c", "d").ConfigureAwait(false);
            var removed = await rig
                .Queue.RemoveAllAsync(track => track.Info.Identifier is "b" or "d")
                .ConfigureAwait(false);

            await Assert.That(removed).IsEqualTo(2);
            await Assert.That(Ids(rig.Queue.Upcoming)).IsEqualTo("c");
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
                    Make("a"),
                    Make("b", 60000),
                    Make("c", 40000),
                    Make("s", 500000, stream: true),
                ])
                .ConfigureAwait(false);

            await Assert.That(rig.Queue.TotalDuration).IsEqualTo(TimeSpan.FromSeconds(100));
        }
    }

    [Test]
    public async Task NextChangedFollowsTheHeadOfTheQueue()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await rig.Queue.RemoveAsync(0).ConfigureAwait(false);

            await Assert
                .That(
                    string.Join(
                        ",",
                        rig.Log.Where(entry => entry.StartsWith("next:", StringComparison.Ordinal))
                    )
                )
                .IsEqualTo("next:b,next:none");
        }
    }

    [Test]
    public async Task AThrowingHandlerGoesToHandlerError()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Queue.Advanced += (_, _) => throw new InvalidOperationException("boom");
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await rig.Queue.SkipAsync().ConfigureAwait(false);

            await Assert.That(rig.Log.Count(entry => entry == "error:boom")).IsEqualTo(2);
            await Assert.That(rig.CurrentId).IsEqualTo("b");
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
    public async Task AStuckTrackThatIsNotTheQueuesIsIgnored()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await PushStuckAsync(rig, "stranger").ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);

            await Assert.That(rig.CurrentId).IsEqualTo("a");
            await Assert.That(rig.Log.Contains("failed:stranger")).IsFalse();
            await Assert.That(rig.Played).IsEqualTo("a");
        }
    }

    [Test]
    public async Task TheQueueKeepsPlayingAfterTheNodeDropsAndResumes()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var ready = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            rig.Node.Ready += (_, _) =>
            {
                ready.TrySetResult();
                return Task.CompletedTask;
            };
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await rig.Server.DropConnectionAsync(1012, "restart").ConfigureAwait(false);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await PushEndAsync(rig, "a", "finished").ConfigureAwait(false);

            await Eventually(() => rig.CurrentId == "b").ConfigureAwait(false);
            await Assert.That(rig.Connection.GetQueue()).IsSameReferenceAs(rig.Queue);
            await Assert.That(rig.Played).IsEqualTo("a,b");
        }
    }

    [Test]
    public async Task TheQueueDetachesWhenTheNodeIsStoppedAndStartedAgain()
    {
        var rig = await StartAsync(resume: false, rescue: true).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var invalidated = 0;
            rig.Connection.GuildConnectionInvalidated += (_, _) =>
            {
                Interlocked.Increment(ref invalidated);
                return Task.CompletedTask;
            };
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await rig.Node.DisconnectAsync().ConfigureAwait(false);
            await rig.Extension.ConnectAllNodesAsync().ConfigureAwait(false);

            await Eventually(() => Volatile.Read(ref invalidated) == 1).ConfigureAwait(false);
            await Assert.That(rig.Connection.GetQueue()).IsNull();
            await Assert
                .That(async () => await rig.EnqueueAsync("c").ConfigureAwait(false))
                .Throws<InvalidOperationException>();
        }
    }

    [Test]
    public async Task TheQueueSurvivesARescuedNode()
    {
        var rig = await StartAsync(resume: false, rescue: true).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var restored = 0;
            rig.Connection.GuildConnectionRestored += (_, _) =>
            {
                Interlocked.Increment(ref restored);
                return Task.CompletedTask;
            };
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await rig.Server.DropConnectionAsync(1012, "restart").ConfigureAwait(false);

            await Eventually(() => Volatile.Read(ref restored) == 1).ConfigureAwait(false);
            await Assert.That(rig.Connection.GetQueue()).IsSameReferenceAs(rig.Queue);
            await rig.EnqueueAsync("c").ConfigureAwait(false);
            await Assert.That(Ids(rig.Queue.Upcoming)).IsEqualTo("b,c");
        }
    }

    [Test]
    public async Task TheQueueIsClearedWhenTheNodeRestoresWithoutTheTrack()
    {
        var rig = await StartAsync(resume: false).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var resent = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            rig.Connection.GuildConnectionRestored += (_, e) =>
            {
                resent.TrySetResult(e.PlaybackResent);
                return Task.CompletedTask;
            };
            await rig.EnqueueAsync("a", "b", "c").ConfigureAwait(false);
            await rig.Queue.SkipAsync().ConfigureAwait(false);
            rig.Queue.Loop = LoopMode.Queue;
            await rig.Server.DropConnectionAsync(1012, "restart").ConfigureAwait(false);

            await Assert
                .That(await resent.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsFalse();
            using (Assert.Multiple())
            {
                await Assert.That(rig.CurrentId).IsNull();
                await Assert.That(rig.Queue.Upcoming.Count).IsEqualTo(0);
                await Assert.That(rig.Queue.History.Count).IsEqualTo(0);
                await Assert.That(rig.Queue.Loop).IsEqualTo(LoopMode.Off);
                await Assert.That(rig.Connection.GetQueue()).IsSameReferenceAs(rig.Queue);
            }

            await rig.EnqueueAsync("d").ConfigureAwait(false);
            await Assert.That(rig.CurrentId).IsEqualTo("d");
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
    public async Task EnqueueAddsNothingWhenThePlayFails()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.FailPlays.Value = true;
            await Assert
                .That(async () => await rig.EnqueueAsync("a", "b").ConfigureAwait(false))
                .Throws<RestException>();
            await Assert.That(rig.CurrentId).IsNull();
            await Assert.That(rig.Queue.Upcoming.Count).IsEqualTo(0);

            rig.FailPlays.Value = false;
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await Assert.That(rig.CurrentId).IsEqualTo("a");
            await Assert.That(Ids(rig.Queue.Upcoming)).IsEqualTo("b");
        }
    }

    [Test]
    public async Task ALoadFailureRaisesTrackFailedWhenTheNextPlayFails()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            rig.FailPlays.Value = true;
            await PushEndAsync(rig, "a", "loadFailed").ConfigureAwait(false);

            await Eventually(() => rig.Log.Contains("failed:a")).ConfigureAwait(false);
            await Assert.That(rig.CurrentId).IsNull();
            await Assert.That(Ids(rig.Queue.Upcoming)).IsEqualTo("b");
        }
    }

    [Test]
    public async Task TheLoopGoesBackToOffWhenTheLastTrackFails()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a").ConfigureAwait(false);
            rig.Queue.Loop = LoopMode.Track;
            await PushEndAsync(rig, "a", "loadFailed").ConfigureAwait(false);

            await Eventually(() => rig.Log.Contains("ended")).ConfigureAwait(false);
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
    public async Task PreviousWithoutHistoryPlaysNothing()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            var previous = await rig.Queue.PreviousAsync().ConfigureAwait(false);

            await Assert.That(previous).IsNull();
            await Assert.That(rig.CurrentId).IsEqualTo("a");
            await Assert.That(rig.Played).IsEqualTo("a");
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
    public async Task ClearKeepsTheCurrentTrack()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b", "c").ConfigureAwait(false);
            await rig.Queue.ClearAsync().ConfigureAwait(false);

            await Assert.That(rig.CurrentId).IsEqualTo("a");
            await Assert.That(rig.Queue.Upcoming.Count).IsEqualTo(0);
            await Assert.That(rig.Log.Contains("next:none")).IsTrue();
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

    [Test]
    public async Task EnqueueOfNoTracksChangesNothing()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var started = await rig.Queue.EnqueueAsync([]).ConfigureAwait(false);

            await Assert.That(started).IsNull();
            await Assert.That(rig.Queue.Current).IsNull();
            await Assert.That(rig.Played).IsEmpty();
        }
    }

    [Test]
    public async Task RemoveAllWithoutAMatchRemovesNothing()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            var removed = await rig
                .Queue.RemoveAllAsync(track => track.Info.Identifier == "z")
                .ConfigureAwait(false);

            await Assert.That(removed).IsEqualTo(0);
            await Assert.That(Ids(rig.Queue.Upcoming)).IsEqualTo("b");
        }
    }

    [Test]
    public async Task TheLimitsKeepTheValuesSetAndRejectInvalidOnes()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Queue.MaxLength = 3;
            rig.Queue.HistoryLimit = 0;

            await Assert.That(rig.Queue.MaxLength).IsEqualTo(3);
            await Assert.That(rig.Queue.HistoryLimit).IsEqualTo(0);
            await Assert
                .That(() => rig.Queue.HistoryLimit = -1)
                .Throws<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task TimeUntilAddsWhatPlaysBefore()
    {
        var time = new FakeTimeProvider();
        var rig = await StartAsync(time: time).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig
                .Queue.EnqueueAsync([
                    Make("a", 90000),
                    Make("b", 60000),
                    Make("s", 500000, stream: true),
                    Make("c", 40000),
                ])
                .ConfigureAwait(false);

            using (Assert.Multiple())
            {
                await Assert.That(rig.Queue.TimeUntil(0)).IsEqualTo(TimeSpan.FromSeconds(90));
                await Assert.That(rig.Queue.TimeUntil(2)).IsEqualTo(TimeSpan.FromSeconds(150));
                await Assert.That(rig.Queue.TimeUntil(3)).IsNull();
                await Assert.That(rig.Queue.TimeUntil(-1)).IsNull();
            }

            time.Advance(TimeSpan.FromMinutes(2));
            await Assert.That(rig.Queue.TimeUntil(1)).IsEqualTo(TimeSpan.FromSeconds(60));

            rig.Queue.Loop = LoopMode.Track;
            await Assert.That(rig.Queue.TimeUntil(0)).IsNull();
        }
    }

    [Test]
    public async Task AStuckLastTrackStopsThePlayer()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a").ConfigureAwait(false);
            await PushStuckAsync(rig, "a").ConfigureAwait(false);

            await Eventually(() =>
                    rig.Server.RestCalls.Any(call =>
                        call.Body?.Contains(
                            "\"track\":{\"encoded\":null}",
                            StringComparison.Ordinal
                        ) == true
                    )
                )
                .ConfigureAwait(false);
            await Assert.That(rig.Log.Contains("failed:a")).IsTrue();
            await Assert.That(rig.Queue.Current).IsNull();
        }
    }

    [Test]
    public async Task AGaplessEndMovesTheQueueWithoutPlaying()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b", "c").ConfigureAwait(false);
            await PushEndAsync(rig, "a", "gapless").ConfigureAwait(false);

            await Eventually(() => rig.Log.Contains("advanced:b")).ConfigureAwait(false);
            await Assert.That(rig.CurrentId).IsEqualTo("b");
            await Assert.That(Ids(rig.Queue.History)).IsEqualTo("a");
            await Assert.That(Ids(rig.Queue.Upcoming)).IsEqualTo("c");
            await Assert.That(rig.Played).IsEqualTo("a");
        }
    }

    [Test]
    public async Task AnUpcomingTrackStartingAfterAGaplessEndIsAdopted()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.EnqueueAsync("a", "b", "c").ConfigureAwait(false);
            await PushEndAsync(rig, "a", "crossfading").ConfigureAwait(false);
            await Eventually(() => rig.CurrentId == "b").ConfigureAwait(false);
            await PushStartAsync(rig, "c").ConfigureAwait(false);

            await Eventually(() => rig.Log.Contains("advanced:c")).ConfigureAwait(false);
            await Assert.That(rig.CurrentId).IsEqualTo("c");
            await Assert.That(Ids(rig.Queue.Upcoming)).IsEqualTo("b");
            await Assert.That(rig.Played).IsEqualTo("a");
        }
    }

    [Test]
    public async Task ThePreloadFollowsTheHeadAndIsSentAgainAfterAnotherTrackStarts()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Queue.PreloadNext = true;
            await rig.EnqueueAsync("a", "b", "c").ConfigureAwait(false);
            await rig.Queue.MoveAsync(1, 0).ConfigureAwait(false);

            int PreloadsOfC() =>
                rig.Server.RestCalls.Count(call =>
                    call.Method == "PATCH"
                    && call.Body?.Contains(
                        "\"nextTrack\":{\"encoded\":\"enc-c\"",
                        StringComparison.Ordinal
                    ) == true
                );

            await Assert.That(PreloadsOfC()).IsEqualTo(1);

            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            rig.Connection.TrackStarted += (_, _) =>
            {
                started.TrySetResult();
                return Task.CompletedTask;
            };
            await PushStartAsync(rig, "z").ConfigureAwait(false);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            rig.Queue.Loop = LoopMode.Off;

            await Eventually(() => PreloadsOfC() == 2).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ShufflePlaysEveryTrackOnce()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Queue.Shuffle = true;
            await rig.EnqueueAsync("a", "b", "c", "d", "e").ConfigureAwait(false);
            for (var played = 1; played < 5; played++)
            {
                var current = rig.CurrentId;
                await PushEndAsync(rig, current!, "finished").ConfigureAwait(false);
                await Eventually(() => rig.CurrentId != current).ConfigureAwait(false);
            }

            var order = rig.Played.Split(',');
            await Assert.That(order.Length).IsEqualTo(5);
            await Assert.That(string.Join(",", order.Order())).IsEqualTo("a,b,c,d,e");
        }
    }

    [Test]
    public async Task ShufflePlaysTheTrackItPreloaded()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Queue.Shuffle = true;
            rig.Queue.PreloadNext = true;
            await rig.EnqueueAsync("a", "b", "c", "d", "e").ConfigureAwait(false);

            string? LastPreloaded() =>
                rig
                    .Server.RestCalls.Where(call => call.Method == "PATCH")
                    .Select(call => PreloadedId(call.Body))
                    .LastOrDefault(id => id is not null);

            for (var played = 1; played < 5; played++)
            {
                await Eventually(() =>
                        LastPreloaded() is { } preloaded
                        && preloaded == rig.Queue.Upcoming[0].Info.Identifier
                    )
                    .ConfigureAwait(false);
                var next = LastPreloaded();
                var current = rig.CurrentId;
                await PushEndAsync(rig, current!, "finished").ConfigureAwait(false);
                await Eventually(() => rig.CurrentId != current).ConfigureAwait(false);

                await Assert.That(rig.CurrentId).IsEqualTo(next);
            }
        }
    }

    [Test]
    public async Task ShuffleKnowsOnlyTheTimeUntilTheNextTrack()
    {
        var rig = await StartAsync(time: new FakeTimeProvider()).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Queue.Shuffle = true;
            await rig.EnqueueAsync("a", "b", "c").ConfigureAwait(false);

            await Assert.That(rig.Queue.TimeUntil(0)).IsEqualTo(TimeSpan.FromSeconds(100));
            await Assert.That(rig.Queue.TimeUntil(1)).IsNull();
        }
    }

    [Test]
    public async Task ShuffleStaysOnAfterAStop()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Queue.Shuffle = true;
            await rig.EnqueueAsync("a", "b").ConfigureAwait(false);
            await rig.Queue.StopAsync().ConfigureAwait(false);

            await Assert.That(rig.Queue.Shuffle).IsTrue();
        }
    }
}
