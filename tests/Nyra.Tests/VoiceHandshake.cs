using Nyra.Abstractions;
using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class VoiceHandshake
{
    private sealed class StuckSendBridge : IDiscordBridge
    {
        private readonly TaskCompletionSource _send = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated
        {
            add { }
            remove { }
        }

        public event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated
        {
            add { }
            remove { }
        }

        public Microsoft.Extensions.Logging.ILogger? Logger => null;

        public Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        ) => _send.Task;

        public Task<ClientInformation> WaitForReadyAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ClientInformation(FakeBridge.UserId));
    }

    private static async Task<Stack> StartAsync(
        TimeSpan? handshakeTimeout = null,
        FakeNodeServer? server = null
    )
    {
        server ??= new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "handshake",
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
    public async Task LeaveEchoCycle()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await conn.DisconnectAsync().ConfigureAwait(false);

            bridge.FireVoiceState(777, null, "vsess");

            bridge.FireVoiceState(777, null, "vsess");

            var again = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await Assert.That(again.ChannelId).IsEqualTo(555);
            await Assert.That(again.IsConnected).IsTrue();
            await again.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task StuckSends()
    {
        var server = new FakeNodeServer();
        var bridge = new StuckSendBridge();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "stuck",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromMilliseconds(600),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        AcceptPlayerPatch(server);

        var connect = node.ConnectAsync(777, 555);
        await Assert
            .That(async () =>
                await connect.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false)
            )
            .Throws<VoiceHandshakeException>();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }
}
