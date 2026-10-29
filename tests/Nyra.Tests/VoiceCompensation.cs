using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class VoiceCompensation
{
    private sealed class FaultyLeaveBridge : FakeBridge
    {
        public bool FaultLeaves { get; set; }

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
            return base.SendVoiceUpdateAsync(
                guildId,
                channelId,
                selfDeaf,
                selfMute,
                cancellationToken
            );
        }
    }

    private sealed class CancellingBridge : FakeBridge
    {
        public override Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        ) => throw new OperationCanceledException("the bridge is disposing");
    }

    private static async Task<Stack> StartAsync(
        TimeSpan handshakeTimeout,
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
                Name = "voice-compensation",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = handshakeTimeout,
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
    public async Task FaultedCompensationWithOwedUnit()
    {
        var bridge = new FaultyLeaveBridge();
        var stack = await StartAsync(TimeSpan.FromSeconds(8), bridge).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await conn.DisconnectAsync().ConfigureAwait(false);

            bridge.FaultLeaves = true;
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert
                .That(async () =>
                {
                    var connect = node.ConnectAsync(777, 555, cts.Token);
                    await connect.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                })
                .Throws<OperationCanceledException>();

            await Task.Delay(500).ConfigureAwait(false);
            bridge.FireVoiceState(777, null, "vsess");
            var again = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await Assert.That(again.IsConnected).IsTrue();
            await Assert.That(again.ChannelId).IsEqualTo(555);
            await again.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ExpiredUnitDrainedUnderPending()
    {
        var stack = await StartAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await conn.DisconnectAsync().ConfigureAwait(false);

            await Task.Delay(150).ConfigureAwait(false);
            AcceptPlayerPatch(server);
            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(400).ConfigureAwait(false);
            bridge.FireVoiceState(777, null, "late-echo");

            await Assert
                .That(async () =>
                {
                    await connect.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                })
                .Throws<VoiceHandshakeException>();

            bridge.FireVoiceState(777, null, "compensation-echo");
            var again = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await Assert.That(again.IsConnected).IsTrue();
            await again.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task StaleJoinStateSwallowed()
    {
        var stack = await StartAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            AcceptPlayerPatch(server);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            try
            {
                var connect = node.ConnectAsync(777, 555, cts.Token);
                await connect.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            await Task.Delay(300).ConfigureAwait(false);

            bridge.FireVoiceState(777, 555, "dead-session");
            bridge.FireVoiceState(777, null, "compensation-echo");
            var again = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await Assert.That(again.IsConnected).IsTrue();
            await Assert.That(again.ChannelId).IsEqualTo(555);
            await again.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task MoveMidHandshake()
    {
        var stack = await StartAsync(TimeSpan.FromMilliseconds(400)).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            AcceptPlayerPatch(server);

            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 999, "vsess");

            await Assert
                .That(async () =>
                {
                    await connect.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                })
                .Throws<VoiceHandshakeException>();

            bridge.FireVoiceState(777, null, "compensation-echo");
            var again = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await Assert.That(again.IsConnected).IsTrue();
            await again.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task BridgeCancelledSend()
    {
        var bridge = new CancellingBridge();
        var stack = await StartAsync(TimeSpan.FromSeconds(8), bridge).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            AcceptPlayerPatch(stack.Server);

            await Assert
                .That(async () =>
                {
                    var connect = stack.Node.ConnectAsync(777, 555);
                    await connect.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                })
                .Throws<VoiceHandshakeException>();
        }
    }

    [Test]
    public async Task HungSendTimesOut()
    {
        var bridge = new FakeBridge { HangVoiceSends = true };
        var stack = await StartAsync(TimeSpan.FromMilliseconds(400), bridge).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            AcceptPlayerPatch(stack.Server);

            var connect = stack.Node.ConnectAsync(777, 555);
            await Assert
                .That(async () =>
                    await connect.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false)
                )
                .Throws<VoiceHandshakeException>();
        }
    }
}
