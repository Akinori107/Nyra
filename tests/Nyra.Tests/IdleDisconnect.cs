using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Nyra.Entities;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class IdleDisconnect
{
    private static readonly TimeSpan Timeout5 = TimeSpan.FromMinutes(5);

    private sealed class Rig : IAsyncDisposable
    {
        public required NyraExtension Extension { get; init; }

        public required FakeNodeServer Server { get; init; }

        public required FakeBridge Bridge { get; init; }

        public required Node Node { get; init; }

        public required GuildConnection Connection { get; init; }

        public required FakeTimeProvider Time { get; init; }

        public ConcurrentQueue<string> Log { get; } = [];

        public bool Connected => Node.GetConnection(777) is not null;

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private static string TrackJson(string id) =>
        $$$"""{"encoded":"enc-{{{id}}}","info":{"identifier":"{{{id}}}","author":"a","title":"{{{id}}}","length":100000,"isStream":false,"isSeekable":true,"position":0}}""";

    private static Track Make(string id) =>
        JsonSerializer.Deserialize<Track>(TrackJson(id), NyraJson.SerializerOptions)!;

    private static async Task<Rig> StartAsync(TimeSpan? idle = null)
    {
        var server = new FakeNodeServer();
        var time = new FakeTimeProvider();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var options = new NodeOptions
        {
            Name = "idle",
            TimeProvider = time,
            VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
            ReadyTimeout = TimeSpan.FromSeconds(5),
        };
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            idle is { } timeout ? options with { IdleTimeout = timeout } : options
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
        server.On("DELETE", PlayerPath, _ => new FakeRestResponse(204, string.Empty));

        var rig = new Rig
        {
            Extension = extension,
            Server = server,
            Bridge = bridge,
            Node = node,
            Connection = connection,
            Time = time,
        };
        connection.IdleTimedOut += (_, e) =>
        {
            rig.Log.Enqueue($"idle:{e.GuildId}:{e.ChannelId}:{rig.Connected}");
            return Task.CompletedTask;
        };
        return rig;
    }

    private static Task PushEndAsync(Rig rig, string id) =>
        rig.Server.PushAsync(
            $$"""{"op":"event","guildId":"777","type":"TrackEndEvent","track":{{TrackJson(id)}},"reason":"finished"}"""
        );

    private static async Task PlayAsync(Rig rig, string id)
    {
        await rig.Connection.PlayAsync(Make(id)).ConfigureAwait(false);
        await Assert.That(rig.Connection.CurrentTrack?.Info.Identifier).IsEqualTo(id);
    }

    [Test]
    public async Task LeavesAfterTheTimeoutWithoutATrack()
    {
        var rig = await StartAsync(Timeout5).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await RunForAsync(rig.Time, TimeSpan.FromMinutes(4.9)).ConfigureAwait(false);
            await Assert.That(rig.Connected).IsTrue();
            await Assert.That(rig.Log).IsEmpty();

            var left = await AdvanceUntilAsync(
                    rig.Time,
                    () => !rig.Connected,
                    TimeSpan.FromMinutes(1)
                )
                .ConfigureAwait(false);

            await Assert.That(left).IsTrue();
            await Assert.That(rig.Log).IsEquivalentTo(["idle:777:555:True"]);
            await Assert.That(rig.Bridge.VoiceSends.Contains((777, null))).IsTrue();
        }
    }

    [Test]
    public async Task StaysWhileATrackPlays()
    {
        var rig = await StartAsync(Timeout5).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await PlayAsync(rig, "a").ConfigureAwait(false);

            await RunForAsync(rig.Time, TimeSpan.FromMinutes(12)).ConfigureAwait(false);

            await Assert.That(rig.Connected).IsTrue();
            await Assert.That(rig.Log).IsEmpty();
        }
    }

    [Test]
    public async Task APausedTrackStillCountsAsPlaying()
    {
        var rig = await StartAsync(Timeout5).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await PlayAsync(rig, "a").ConfigureAwait(false);
            await rig.Connection.PauseAsync().ConfigureAwait(false);

            await RunForAsync(rig.Time, TimeSpan.FromMinutes(12)).ConfigureAwait(false);

            await Assert.That(rig.Connected).IsTrue();
            await Assert.That(rig.Log).IsEmpty();
        }
    }

    [Test]
    public async Task TheCountStartsWhenTheTrackEnds()
    {
        var rig = await StartAsync(Timeout5).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await RunForAsync(rig.Time, TimeSpan.FromMinutes(4)).ConfigureAwait(false);
            await PlayAsync(rig, "a").ConfigureAwait(false);
            await PushEndAsync(rig, "a").ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.CurrentTrack is null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await RunForAsync(rig.Time, TimeSpan.FromMinutes(4)).ConfigureAwait(false);
            await Assert.That(rig.Connected).IsTrue();

            var left = await AdvanceUntilAsync(
                    rig.Time,
                    () => !rig.Connected,
                    TimeSpan.FromMinutes(2)
                )
                .ConfigureAwait(false);
            await Assert.That(left).IsTrue();
        }
    }

    [Test]
    public async Task AHandlerThatPlaysKeepsTheConnection()
    {
        var rig = await StartAsync(Timeout5).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            rig.Connection.IdleTimedOut += async (_, _) =>
                await rig.Connection.PlayAsync(Make("kept")).ConfigureAwait(false);

            var fired = await AdvanceUntilAsync(
                    rig.Time,
                    () => !rig.Log.IsEmpty,
                    TimeSpan.FromMinutes(6)
                )
                .ConfigureAwait(false);
            await RunForAsync(rig.Time, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            await Assert.That(fired).IsTrue();
            await Assert.That(rig.Connected).IsTrue();
            await Assert.That(rig.Connection.CurrentTrack?.Info.Identifier).IsEqualTo("kept");
        }
    }

    [Test]
    public async Task ADisconnectBeforeTheTimeoutStopsIt()
    {
        var rig = await StartAsync(Timeout5).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await rig.Connection.DisconnectAsync().ConfigureAwait(false);

            await RunForAsync(rig.Time, TimeSpan.FromMinutes(12)).ConfigureAwait(false);

            await Assert.That(rig.Log).IsEmpty();
        }
    }

    [Test]
    public async Task OffByDefault()
    {
        var rig = await StartAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await Assert.That(rig.Node.Options.IdleTimeout).IsEqualTo(Timeout.InfiniteTimeSpan);

            await RunForAsync(rig.Time, TimeSpan.FromMinutes(30)).ConfigureAwait(false);

            await Assert.That(rig.Connected).IsTrue();
            await Assert.That(rig.Log).IsEmpty();
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1000)]
    public async Task ANonPositiveTimeoutIsRejected(int milliseconds)
    {
        var options = new NodeOptions { IdleTimeout = TimeSpan.FromMilliseconds(milliseconds) };

        await Assert
            .That(() => new Node(new Endpoint("127.0.0.1", 1, "p"), options))
            .Throws<ArgumentOutOfRangeException>();
    }
}
