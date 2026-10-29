using System.Text.Json;
using Nyra.Entities;
using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class ConnectionLifecycle
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

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
                Name = "lifecycle",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
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

    [Test]
    public async Task PlaySurface()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            await Assert
                .That(async () =>
                {
                    await conn.PlayAsync(null!).ConfigureAwait(false);
                })
                .Throws<ArgumentNullException>();

            await Assert
                .That(async () =>
                {
                    await conn.PlayAsync(new Track()).ConfigureAwait(false);
                })
                .Throws<ArgumentException>();

            await Assert
                .That(async () =>
                {
                    await conn.PlayIdentifierAsync("ph8qt3dhvwo").ConfigureAwait(false);
                })
                .Throws<NotSupportedException>();

            await conn.PlayAsync(Track()).ConfigureAwait(false);
            await conn.PauseAsync().ConfigureAwait(false);
            var pauseBody = server
                .RestCalls.Reverse()
                .First(c => c is { Method: "PATCH", Path: PlayerPath })
                .Body;
            await Assert.That(pauseBody).IsEqualTo("""{"paused":true}""");

            await conn.ResumeAsync().ConfigureAwait(false);
            var resumeBody = server
                .RestCalls.Reverse()
                .First(c => c is { Method: "PATCH", Path: PlayerPath })
                .Body;
            await Assert.That(resumeBody).IsEqualTo("""{"paused":false}""");

            await conn.SeekAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            var seekBody = server
                .RestCalls.Reverse()
                .First(c => c is { Method: "PATCH", Path: PlayerPath })
                .Body;
            await Assert.That(seekBody).IsEqualTo("""{"position":30000}""");

            await conn.SeekAsync(TimeSpan.Zero).ConfigureAwait(false);
            var restartBody = server
                .RestCalls.Reverse()
                .First(c => c is { Method: "PATCH", Path: PlayerPath })
                .Body;
            await Assert.That(restartBody).IsEqualTo("""{"position":0}""");

            await conn.PlayAsync(
                    Track(),
                    new PlayOptions { StartPosition = TimeSpan.FromSeconds(10) }
                )
                .ConfigureAwait(false);
            var playBody = server
                .RestCalls.Reverse()
                .First(c => c is { Method: "PATCH", Path: PlayerPath })
                .Body;
            await Assert.That(playBody).IsNotNull();
            await Assert.That(playBody).Contains("\"position\":10000");

            server.On(
                "GET",
                PlayerPath,
                _ => new FakeRestResponse(
                    200,
                    """{"guildId":"777","track":null,"volume":80,"paused":true,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
                )
            );
            var refreshed = await conn.RefreshAsync().ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(refreshed).IsNotNull();
                await Assert.That(refreshed!.Volume).IsEqualTo(80);
                await Assert.That(refreshed!.IsPaused).IsTrue();
            }

            server.On(
                "PATCH",
                PlayerPath,
                _ => new FakeRestResponse(500, "{\"message\":\"boom\"}")
            );
            await Assert
                .That(async () =>
                {
                    await conn.PlayAsync(Track()).ConfigureAwait(false);
                })
                .Throws<RestException>();

            await stack.Extension.DisposeAsync().ConfigureAwait(false);
            stack.Server.Dispose();
        }
    }

    [Test]
    public async Task SessionInvalidation()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await Assert.That(node.GetConnection(777)).IsNotNull();

            var invalidated = new TaskCompletionSource<ulong>();
            conn.GuildConnectionInvalidated += (_, args) =>
            {
                invalidated.TrySetResult(args.GuildId);
                return Task.CompletedTask;
            };

            await server
                .PushAsync(
                    """{"op":"event","type":"WorkerFailedEvent","workerId":3,"affectedGuilds":["777"]}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                )
                .IsEqualTo(777);
            var dropped = false;
            for (var i = 0; i < 40 && !dropped; i++)
            {
                await Task.Delay(50).ConfigureAwait(false);
                dropped = node.GetConnection(777) is null;
            }
            await Assert.That(dropped).IsTrue();

            await stack.Extension.DisposeAsync().ConfigureAwait(false);
            stack.Server.Dispose();
        }
    }

    [Test]
    public async Task VoiceCloseSurvival()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            var closed = new TaskCompletionSource<int?>();
            conn.WebSocketClosed += (_, args) =>
            {
                closed.TrySetResult(args.Code);
                return Task.CompletedTask;
            };

            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":4006,"reason":"boom","byRemote":true}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsEqualTo(4006);

            await Task.Delay(500).ConfigureAwait(false);
            await Assert.That(node.GetConnection(777)).IsNotNull();
            await Assert.That(conn.IsConnected).IsTrue();

            await conn.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task VoiceCloseGrace()
    {
        var server = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
        var stack = await StartAsync(server).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, sv) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, sv).ConfigureAwait(false);
            await Assert.That(node.IsNodeLink).IsFalse();

            await sv.PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":4006,"reason":"boom","byRemote":true}"""
                )
                .ConfigureAwait(false);

            var tornDown = false;
            for (var i = 0; i < 160 && !tornDown; i++)
            {
                await Task.Delay(100).ConfigureAwait(false);
                tornDown = node.GetConnection(777) is null;
            }
            await Assert.That(tornDown).IsTrue();

            var leaveSent = false;
            for (var i = 0; i < 20 && !leaveSent; i++)
            {
                await Task.Delay(100).ConfigureAwait(false);
                leaveSent = bridge.VoiceSends.Contains((777, null));
            }
            await Assert.That(leaveSent).IsTrue();

            await stack.Extension.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task LavalinkIdentifierPlay()
    {
        var server = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
        var stack = await StartAsync(server).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, sv) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, sv).ConfigureAwait(false);

            await Assert
                .That(async () =>
                {
                    await conn
                        .Effects.SetFadingAsync(new FadingOptions { Enabled = true })
                        .ConfigureAwait(false);
                })
                .Throws<NotSupportedException>();

            sv.On(
                "PATCH",
                PlayerPath,
                _ => new FakeRestResponse(
                    200,
                    "{\"guildId\":\"777\",\"track\":"
                        + TrackJson
                        + ",\"volume\":100,\"paused\":false,\"state\":{\"time\":0,\"position\":0,\"connected\":true,\"ping\":42}}"
                )
            );
            var played = await conn.PlayIdentifierAsync("ph8qt3dhvwo").ConfigureAwait(false);
            await Assert.That(played.Info.Title).IsEqualTo("ATWA");
            var identifierBody = sv
                .RestCalls.Reverse()
                .First(c => c is { Method: "PATCH", Path: PlayerPath })
                .Body;
            await Assert.That(identifierBody).IsNotNull();
            await Assert.That(identifierBody).Contains("\"identifier\":\"ph8qt3dhvwo\"");

            await conn.PlayIdentifierAsync("https://example.com/audio.mp3?t=15")
                .ConfigureAwait(false);
            var offsetBody = sv
                .RestCalls.Reverse()
                .First(c => c is { Method: "PATCH", Path: PlayerPath })
                .Body;
            await Assert.That(offsetBody).IsNotNull();
            await Assert.That(offsetBody).Contains("\"position\":15000");

            await conn.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task PlaybackStampRollbacks()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            var track = Track();

            server.On(
                "PATCH",
                PlayerPath,
                _ => new FakeRestResponse(500, "{\"message\":\"boom\"}")
            );
            await Assert
                .That(async () =>
                {
                    await conn.PauseAsync().ConfigureAwait(false);
                })
                .Throws<RestException>();

            AcceptPlayerPatch(server);
            await conn.PauseAsync().ConfigureAwait(false);
            var pauseBody = server
                .RestCalls.Reverse()
                .First(c => c is { Method: "PATCH", Path: PlayerPath })
                .Body;
            await Assert.That(pauseBody).IsEqualTo("{\"paused\":true}");

            server.On(
                "PATCH",
                PlayerPath,
                _ => new FakeRestResponse(500, "{\"message\":\"boom\"}")
            );
            await Assert
                .That(async () =>
                {
                    await conn.PlayAsync(track).ConfigureAwait(false);
                })
                .Throws<RestException>();
            AcceptPlayerPatch(server);
            await conn.PlayAsync(track).ConfigureAwait(false);

            await conn.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ParkedPatchDrain()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            server.On("DELETE", PlayerPath, _ => new FakeRestResponse(204, string.Empty));

            var parked = new TaskCompletionSource();
            var release = new TaskCompletionSource();
            server.On(
                "PATCH",
                PlayerPath,
                _ =>
                {
                    parked.TrySetResult();
                    release.Task.Wait(TimeSpan.FromSeconds(10));
                    return new FakeRestResponse(
                        200,
                        """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
                    );
                }
            );
            var playing = conn.PlayAsync(Track());
            await parked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            var disconnecting = conn.DisconnectAsync();
            release.TrySetResult();
            await Task.WhenAll(playing, disconnecting).ConfigureAwait(false);

            await Assert.That(node.GetConnection(777)).IsNull();
            var destroyed = false;
            for (var i = 0; i < 40 && !destroyed; i++)
            {
                await Task.Delay(50).ConfigureAwait(false);
                destroyed = server.RestCalls.Any(c => c is { Method: "DELETE", Path: PlayerPath });
            }
            await Assert.That(destroyed).IsTrue();
        }
    }
}
