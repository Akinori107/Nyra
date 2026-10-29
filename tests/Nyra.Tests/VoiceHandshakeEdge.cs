using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class VoiceHandshakeEdge
{
    private sealed class ModeLeaveBridge : FakeBridge
    {
        public bool FaultLeaves { get; set; }

        public bool HangLeaves { get; set; }

        public override Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        )
        {
            if (channelId is null && FaultLeaves)
                throw new InvalidOperationException("the gateway refuses the leave");
            if (channelId is null && HangLeaves)
                return HangSend();
            return base.SendVoiceUpdateAsync(
                guildId,
                channelId,
                selfDeaf,
                selfMute,
                cancellationToken
            );
        }

        private static Task HangSend()
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return tcs.Task;
        }
    }

    private sealed class LeaveFaultingBridge : FakeBridge
    {
        public bool FaultLeaves { get; set; }

        public override Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        ) =>
            channelId is null && FaultLeaves
                ? throw new InvalidOperationException("the gateway refuses the leave")
                : base.SendVoiceUpdateAsync(
                    guildId,
                    channelId,
                    selfDeaf,
                    selfMute,
                    cancellationToken
                );
    }

    private sealed class EchoThenFaultBridge : FakeBridge
    {
        public bool EchoThenFault { get; set; }

        public override Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        )
        {
            if (channelId is null && EchoThenFault)
            {
                FireVoiceState(guildId, null, "dead-session");
                return Task.FromException(new InvalidOperationException("late fault"));
            }
            return base.SendVoiceUpdateAsync(
                guildId,
                channelId,
                selfDeaf,
                selfMute,
                cancellationToken
            );
        }
    }

    private static async Task<Stack> StartAsync(
        TimeSpan? handshakeTimeout = null,
        FakeBridge? bridge = null
    )
    {
        var server = new FakeNodeServer();
        bridge ??= new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "handshake-edges",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(5),
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
    public async Task LateLeaveEcho()
    {
        var stack = await StartAsync(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await conn.DisconnectAsync().ConfigureAwait(false);

            await Task.Delay(600).ConfigureAwait(false);
            bridge.FireVoiceState(777, null, "vsess");
            bridge.FireVoiceServer(777, "late-token", "127.0.0.1");

            var again = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await Assert.That(again.IsConnected).IsTrue();
            await Assert.That(again.ChannelId).IsEqualTo(555);
            await again.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task KickAfterComplete()
    {
        VoiceHandshakeException? stale = null;
        for (var attempt = 0; attempt < 5 && stale is null; attempt++)
        {
            var stack = await StartAsync().ConfigureAwait(false);
            await using (stack.ConfigureAwait(false))
            {
                var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
                AcceptPlayerPatch(server);

                var connect = node.ConnectAsync(777, 555);
                await Task.Delay(100).ConfigureAwait(false);
                bridge.FireVoiceState(777, 555, "vsess");
                bridge.FireVoiceServer(777, "tok", "127.0.0.1");
                bridge.FireVoiceState(777, null, "vsess");

                try
                {
                    await connect.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                }
                catch (VoiceHandshakeException exception)
                {
                    stale = exception;
                }
            }
        }
        await Assert.That(stale).IsNotNull();
    }

    [Test]
    public async Task MoveAfterComplete()
    {
        VoiceHandshakeException? stale = null;
        for (var attempt = 0; attempt < 5 && stale is null; attempt++)
        {
            var stack = await StartAsync().ConfigureAwait(false);
            await using (stack.ConfigureAwait(false))
            {
                var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
                AcceptPlayerPatch(server);

                var connect = node.ConnectAsync(777, 555);
                await Task.Delay(100).ConfigureAwait(false);
                bridge.FireVoiceState(777, 555, "vsess");
                bridge.FireVoiceServer(777, "tok", "127.0.0.1");
                bridge.FireVoiceState(777, 999, "vsess");

                try
                {
                    await connect.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                    var landed = node.GetConnection(777);
                    if (landed is { })
                        await landed.DisconnectAsync().ConfigureAwait(false);
                }
                catch (VoiceHandshakeException exception)
                {
                    stale = exception;
                }
            }
        }
        await Assert.That(stale).IsNotNull();
    }

    [Test]
    public async Task BlankServerUpdate()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            AcceptPlayerPatch(server);

            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess");
            bridge.FireVoiceServer(777, string.Empty, string.Empty);
            bridge.FireVoiceServer(777, "tok", string.Empty);
            await Assert.That(connect.IsCompleted).IsFalse();

            bridge.FireVoiceServer(777, "tok", "127.0.0.1");
            var conn = await connect.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            await Assert.That(conn.IsConnected).IsTrue();
            await conn.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task StaleServerUpdate()
    {
        var stack = await StartAsync(TimeSpan.FromMilliseconds(400)).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            AcceptPlayerPatch(server);

            var dead = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess");
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess-rotated");
            await Assert
                .That(async () =>
                {
                    await dead.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                })
                .Throws<VoiceHandshakeException>();

            bridge.FireVoiceServer(777, "dead-token", "127.0.0.1");

            await Task.Delay(600).ConfigureAwait(false);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await Assert.That(conn.IsConnected).IsTrue();
            await Assert.That(conn.VoiceToken).IsEqualTo("tok");
            await conn.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task FaultedJoinSend()
    {
        var stack = await StartAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            AcceptPlayerPatch(server);
            bridge.FaultVoiceSends = true;

            await Assert
                .That(async () =>
                    await node.ConnectAsync(777, 555)
                        .WaitAsync(TimeSpan.FromSeconds(10))
                        .ConfigureAwait(false)
                )
                .Throws<InvalidOperationException>()
                .WithMessageContaining("refuses the send");
            await Assert.That(node.GetConnection(777)).IsNull();
        }
    }

    [Test]
    public async Task CallerCancellation()
    {
        var stack = await StartAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            AcceptPlayerPatch(server);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert
                .That(async () =>
                {
                    var connect = node.ConnectAsync(777, 555, cts.Token);
                    await connect.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                })
                .Throws<OperationCanceledException>();
            await Assert.That(node.GetConnection(777)).IsNull();
        }
    }

    [Test]
    public async Task FaultedLeaveOnDisconnect()
    {
        var stack = await StartAsync(TimeSpan.FromMilliseconds(400)).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            bridge.FaultVoiceSends = true;
            await conn.DisconnectAsync().ConfigureAwait(false);
            bridge.FaultVoiceSends = false;

            var again = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await Assert.That(again.IsConnected).IsTrue();
            await Assert.That(again.ChannelId).IsEqualTo(555);
            await again.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task FaultedCompensationLeave()
    {
        var bridge = new LeaveFaultingBridge { FaultLeaves = true };
        var stack = await StartAsync(TimeSpan.FromSeconds(8), bridge).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;
            AcceptPlayerPatch(stack.Server);

            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess");
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess-rotated");

            await Assert
                .That(async () =>
                {
                    await connect.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                })
                .Throws<VoiceHandshakeException>();
            await Assert.That(node.GetConnection(777)).IsNull();
        }
    }

    [Test]
    public async Task TwoCoordinatorsOneBridge()
    {
        var bridge = new ModeLeaveBridge { HangLeaves = true };
        bridge.GoReady();
        var serverA = new FakeNodeServer();
        var serverB = new FakeNodeServer();
        var extension = new NyraExtension(bridge);
        foreach (var (server, name) in new[] { (serverA, "co-a"), (serverB, "co-b") })
        {
            var node = new Node(
                new Endpoint("127.0.0.1", server.Port, server.Password),
                new NodeOptions
                {
                    Name = name,
                    Resuming = true,
                    ReconnectDelay = TimeSpan.FromMilliseconds(50),
                    MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                    VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                    ReadyTimeout = TimeSpan.FromSeconds(5),
                }
            );
            extension.AddNode(node);
        }
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var nodes = extension.Nodes.ToArray();
        var first = nodes[0];
        var second = nodes[1];

        async Task<Exception?> FailWithRotationAsync(Node node, FakeNodeServer server)
        {
            AcceptPlayerPatch(server);
            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess");
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess-rotated");
            try
            {
                await connect.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                return null;
            }
            catch (VoiceHandshakeException exception)
            {
                return exception;
            }
        }

        var firstFailure = await FailWithRotationAsync(first, serverA).ConfigureAwait(false);
        var secondFailure = await FailWithRotationAsync(second, serverB).ConfigureAwait(false);
        await Assert.That(firstFailure).IsNotNull();
        await Assert.That(secondFailure).IsNotNull();

        bridge.FireVoiceState(777, null, "vsess");

        bridge.HangLeaves = false;
        bridge.FaultLeaves = true;
        var thirdFailure = await FailWithRotationAsync(first, serverA).ConfigureAwait(false);
        await Assert.That(thirdFailure).IsNotNull();

        await extension.DisposeAsync().ConfigureAwait(false);
        serverA.Dispose();
        serverB.Dispose();
    }

    [Test]
    public async Task EchoThenFaultCompensation()
    {
        var bridge = new EchoThenFaultBridge { EchoThenFault = true };
        var stack = await StartAsync(TimeSpan.FromSeconds(8), bridge).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;
            AcceptPlayerPatch(stack.Server);

            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess");
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess-rotated");

            await Assert
                .That(async () =>
                {
                    await connect.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                })
                .Throws<VoiceHandshakeException>();
            await Assert.That(node.GetConnection(777)).IsNull();

            var conn = await ConnectGuildAsync(bridge, node, stack.Server).ConfigureAwait(false);
            await Assert.That(conn.IsConnected).IsTrue();
            await conn.DisconnectAsync().ConfigureAwait(false);
        }
    }
}
