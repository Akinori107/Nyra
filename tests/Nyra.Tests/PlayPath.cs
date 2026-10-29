using System.Text.Json;
using Nyra.Entities;
using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class PlayPath
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private static string PlayerWithTrack =>
        $$$"""{"guildId":"777","track":{{{TrackJson}}},"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}""";

    private static async Task<Stack> StartAsync(FakeNodeServer? server = null)
    {
        server ??= new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "play",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(200, PlayerWithTrack));
        var connect = node.ConnectAsync(777, 555);
        await Task.Delay(100).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        await connect.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        return new Stack
        {
            Bridge = bridge,
            Extension = extension,
            Node = node,
            Server = server,
        };
    }

    private static Track Track() =>
        JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.SerializerOptions)!;

    private static async Task<RestException> PlayThrowsAsync(Func<Task> play)
    {
        try
        {
            await play().ConfigureAwait(false);
        }
        catch (RestException exception)
        {
            return exception;
        }
        throw new InvalidOperationException("the ambiguous play never threw");
    }

    [Test]
    public async Task PlayOptionGuards()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;
            var track = Track();

            var guards = new PlayOptions[]
            {
                new(EndTime: TimeSpan.Zero),
                new(EndTime: TimeSpan.FromSeconds(-1)),
                new(StartPosition: TimeSpan.FromSeconds(10), EndTime: TimeSpan.FromSeconds(5)),
                new(AudioTrackId: " "),
                new(UserData: JsonDocument.Parse("[1]").RootElement),
            };
            var patchesBefore = stack.Server.RestCalls.Count(call => call.Method == "PATCH");
            foreach (var options in guards)
                await Assert
                    .That(async () => await conn.PlayAsync(track, options).ConfigureAwait(false))
                    .Throws<ArgumentException>();
            await Assert
                .That(stack.Server.RestCalls.Count(call => call.Method == "PATCH"))
                .IsEqualTo(patchesBefore);
        }
    }

    [Test]
    public async Task AudioTrackIdGuardFlavors()
    {
        var lava = await StartAsync(new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" })
            .ConfigureAwait(false);
        await using (lava.ConfigureAwait(false))
        {
            var lavaConn = lava.Node.GetConnection(777)!;
            await Assert
                .That(async () =>
                    await lavaConn
                        .PlayAsync(Track(), new PlayOptions { AudioTrackId = "x" })
                        .ConfigureAwait(false)
                )
                .Throws<NotSupportedException>();

            var link = await StartAsync().ConfigureAwait(false);
            await using (link.ConfigureAwait(false))
            {
                var linkConn = link.Node.GetConnection(777)!;
                await Assert
                    .That(async () =>
                        await linkConn
                            .PlayAsync(Track(), new PlayOptions { AudioTrackId = "x" })
                            .ConfigureAwait(false)
                    )
                    .ThrowsNothing();
                await Assert
                    .That(link.Server.RestCalls.Last(call => call is { Method: "PATCH" }).Body)
                    .Contains("\"audioTrackId\":\"x\"");
            }
        }
    }

    [Test]
    public async Task SameTrackRestart()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;
            var track = Track();

            await conn.PlayAsync(track).ConfigureAwait(false);
            await Assert.That(conn.CurrentTrack?.Encoded).IsEqualTo(track.Encoded);

            var patches = 0;
            stack.Server.On(
                "PATCH",
                PlayerPath,
                call =>
                {
                    patches++;
                    return new FakeRestResponse(200, PlayerWithTrack);
                }
            );
            await conn.PlayAsync(track).ConfigureAwait(false);
            await Assert.That(patches).IsGreaterThanOrEqualTo(2);
            var bodies = stack
                .Server.RestCalls.Where(c => c is { Method: "PATCH", Path: PlayerPath })
                .Select(c => c.Body ?? string.Empty)
                .TakeLast(2)
                .ToArray();
            await Assert.That(bodies.Length).IsEqualTo(2);
            await Assert.That(bodies[0]).Contains("\"encoded\":null");
            await Assert.That(bodies[1]).Contains("QXRsZWFzdA==");
        }
    }

    [Test]
    public async Task AmbiguousLanding()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;
            stack.Server.On("PATCH", PlayerPath, 200, "@");
            stack.Server.On("GET", PlayerPath, 200, PlayerWithTrack);
            var exception = await PlayThrowsAsync(() => conn.PlayAsync(Track()))
                .ConfigureAwait(false);
            await Assert.That(conn.CurrentTrack?.Encoded).IsEqualTo("QXRsZWFzdA==");
        }

        var renewal = await StartAsync().ConfigureAwait(false);
        await using (renewal.ConfigureAwait(false))
        {
            var conn = renewal.Node.GetConnection(777)!;
            renewal.Server.On("PATCH", PlayerPath, 200, "@");
            renewal.Server.On("GET", PlayerPath, 404, """{"message":"Player not found"}""");
            var exception = await PlayThrowsAsync(() => conn.PlayAsync(Track()))
                .ConfigureAwait(false);
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.StatusCode).IsEqualTo(200);
            await Assert.That(conn.CurrentTrack).IsNull();
        }
    }

    [Test]
    public async Task IdentifierLanding()
    {
        var stack = await StartAsync(new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" })
            .ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;
            var track = Track();

            await conn.PlayAsync(track).ConfigureAwait(false);
            await Assert.That(conn.CurrentTrack?.Encoded).IsEqualTo(track.Encoded);

            stack.Server.On("PATCH", PlayerPath, 200, "@");
            stack.Server.On("GET", PlayerPath, 200, PlayerWithOtherTrack);
            var landed = await PlayThrowsAsync(() => conn.PlayIdentifierAsync("ytsearch:atwa"))
                .ConfigureAwait(false);
            await Assert.That(conn.CurrentTrack?.Info.Title).IsEqualTo("Other");

            stack.Server.On("PATCH", PlayerPath, 200, "@");
            stack.Server.On("GET", PlayerPath, 200, PlayerWithOtherTrack);
            var missed = await PlayThrowsAsync(() => conn.PlayIdentifierAsync("ytsearch:atwa"))
                .ConfigureAwait(false);
            await Assert.That(missed).IsNotNull();
            await Assert.That(missed!.StatusCode).IsEqualTo(200);
            await Assert.That(conn.CurrentTrack).IsNotNull();
            await Assert.That(conn.CurrentTrack!.Info.Title).IsEqualTo("Other");
        }
    }

    [Test]
    public async Task IdentifierGuards()
    {
        var stack = await StartAsync(new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" })
            .ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;

            await Assert
                .That(async () => await conn.PlayIdentifierAsync(" ").ConfigureAwait(false))
                .Throws<ArgumentException>();

            await Assert
                .That(async () =>
                    await conn.PlayIdentifierAsync("ph8", new PlayOptions { AudioTrackId = "x" })
                        .ConfigureAwait(false)
                )
                .Throws<ArgumentException>();
        }
    }

    private static string PlayerWithOtherTrack =>
        $$$"""{"guildId":"777","track":{"encoded":"T3RoZXI=","info":{"identifier":"other","author":"X","title":"Other","length":1000,"isStream":false,"isSeekable":true,"position":0}},"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}""";

    [Test]
    public async Task HandlerErrorFloor()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;

            var visits = 0;
            var errorVisits = 0;
            conn.TrackStarted += (_, _) =>
            {
                visits++;
                throw new InvalidOperationException("the started handler broke");
            };
            conn.TrackStarted += (_, _) =>
            {
                visits += 10;
                return Task.CompletedTask;
            };
            conn.HandlerError += (_, _) =>
            {
                errorVisits++;
                throw new InvalidOperationException("the error handler broke");
            };

            await stack
                .Server.PushAsync(
                    $$"""{"op":"event","guildId":"777","type":"TrackStartEvent","track":{{TrackJson}}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(() => Volatile.Read(ref visits) == 11)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Assert
                .That(() => Volatile.Read(ref errorVisits) == 1)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        }
    }
}
