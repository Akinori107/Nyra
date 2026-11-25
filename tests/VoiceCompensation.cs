using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class VoiceCompensation
{
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
