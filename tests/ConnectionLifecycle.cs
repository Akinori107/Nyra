using Microsoft.Extensions.Time.Testing;
using Nyra.Entities;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class ConnectionLifecycle
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private static async Task<Stack> StartAsync(
        FakeNodeServer? server = null,
        TimeProvider? time = null
    )
    {
        server ??= new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge, time);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "lifecycle",
                TimeProvider = time ?? TimeProvider.System,
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
            await Assert
                .That(() => node.GetConnection(777) is null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(2));

            await stack.Extension.DisposeAsync().ConfigureAwait(false);
            stack.Server.Dispose();
        }
    }

    [Test]
    public async Task VoiceCloseGrace()
    {
        var server = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
        var time = new FakeTimeProvider();
        var stack = await StartAsync(server, time).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, sv) = (stack.Bridge, stack.Node, stack.Server);
            await ConnectGuildAsync(bridge, node, sv).ConfigureAwait(false);
            await Assert.That(node.IsNodeLink).IsFalse();

            await sv.PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":4006,"reason":"boom","byRemote":true}"""
                )
                .ConfigureAwait(false);

            var tornDown = await AdvanceUntilAsync(
                    time,
                    () => node.GetConnection(777) is null,
                    TimeSpan.FromSeconds(30)
                )
                .ConfigureAwait(false);
            await Assert.That(tornDown).IsTrue();

            var leaveSent = await AdvanceUntilAsync(
                    time,
                    () => bridge.VoiceSends.Contains((777, null)),
                    TimeSpan.FromSeconds(2)
                )
                .ConfigureAwait(false);
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

            sv.On("PATCH", PlayerPath, _ => new FakeRestResponse(
                200,
                "{\"guildId\":\"777\",\"track\":"
                    + TrackJson
                    + ",\"volume\":100,\"paused\":false,\"state\":{\"time\":0,\"position\":0,\"connected\":true,\"ping\":42}}"
            ));
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
}
