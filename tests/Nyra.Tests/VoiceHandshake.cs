using Nyra.Abstractions;
using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class VoiceHandshake
{
    private sealed class StuckSendBridge : Nyra.Abstractions.IDiscordBridge
    {
        private readonly TaskCompletionSource _send = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public event EventHandler<Nyra.Abstractions.VoiceServerUpdatedEventArgs>? VoiceServerUpdated
        {
            add { }
            remove { }
        }

        public event EventHandler<Nyra.Abstractions.VoiceStateUpdatedEventArgs>? VoiceStateUpdated
        {
            add { }
            remove { }
        }

        public Microsoft.Extensions.Logging.ILogger? Logger { get; set; }

        public Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        ) => _send.Task;

        public Task<Nyra.Abstractions.ClientInformation> WaitForReadyAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new Nyra.Abstractions.ClientInformation(FakeBridge.UserId));
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
    public async Task SessionRotation()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            AcceptPlayerPatch(server);

            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess");
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess-rotated");

            await Assert
                .That(async () =>
                {
                    await connect.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                })
                .Throws<VoiceHandshakeException>();
        }
    }

    [Test]
    public async Task TimeoutPhases()
    {
        var stack = await StartAsync(TimeSpan.FromMilliseconds(600)).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            AcceptPlayerPatch(stack.Server);
            var connect = stack.Node.ConnectAsync(777, 555);
            await Assert
                .That(async () => await connect.ConfigureAwait(false))
                .Throws<VoiceHandshakeException>();
        }

        var renewal = await StartAsync(TimeSpan.FromMilliseconds(600)).ConfigureAwait(false);
        await using (renewal.ConfigureAwait(false))
        {
            AcceptPlayerPatch(renewal.Server);
            var connect = renewal.Node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            renewal.Bridge.FireVoiceState(777, 555, "vsess");
            await Assert
                .That(async () => await connect.ConfigureAwait(false))
                .Throws<VoiceHandshakeException>();
        }
    }

    [Test]
    public async Task MidJoinMoves()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            AcceptPlayerPatch(server);
            var first = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            var second = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess");
            bridge.FireVoiceServer(777, "tok", "127.0.0.1");
            var conn = await first.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            await Assert.That(conn.IsConnected).IsTrue();
            var again = await second.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            await Assert.That(ReferenceEquals(again, conn)).IsTrue();
            await conn.DisconnectAsync().ConfigureAwait(false);
        }

        var renewal = await StartAsync().ConfigureAwait(false);
        await using (renewal.ConfigureAwait(false))
        {
            var (bridge, node, server) = (renewal.Bridge, renewal.Node, renewal.Server);
            AcceptPlayerPatch(server);
            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess");
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, null, "vsess");
            await Assert
                .That(async () =>
                    await connect.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false)
                )
                .Throws<VoiceHandshakeException>();
        }

        var renewal2 = await StartAsync().ConfigureAwait(false);
        await using (renewal2.ConfigureAwait(false))
        {
            var (bridge, node, server) = (renewal2.Bridge, renewal2.Node, renewal2.Server);
            AcceptPlayerPatch(server);
            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess");
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 999, "vsess");
            await Assert
                .That(async () =>
                    await connect.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false)
                )
                .Throws<VoiceHandshakeException>();
        }

        var renewal3 = await StartAsync(TimeSpan.FromMilliseconds(600)).ConfigureAwait(false);
        await using (renewal3.ConfigureAwait(false))
        {
            var (bridge, node, server) = (renewal3.Bridge, renewal3.Node, renewal3.Server);
            AcceptPlayerPatch(server);
            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, null, "vsess");
            await Assert
                .That(async () =>
                {
                    await connect.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                })
                .Throws<VoiceHandshakeException>();
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

    [Test]
    public async Task StaleEchoAndRemovalMidJoin()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await conn.DisconnectAsync().ConfigureAwait(false);
            bridge.FireVoiceState(777, 999, "vsess");
            bridge.FireVoiceState(777, null, "vsess");
            var again = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await Assert.That(again.IsConnected).IsTrue();
            await again.DisconnectAsync().ConfigureAwait(false);

            var removalServer = new FakeNodeServer();
            var removalBridge = new RemovalBridge();
            removalBridge.GoReady();
            var removalExtension = new NyraExtension(removalBridge);
            var removalNode = new Node(
                new Endpoint("127.0.0.1", removalServer.Port, removalServer.Password),
                new NodeOptions
                {
                    Name = "handshake-removal",
                    Resuming = true,
                    ReconnectDelay = TimeSpan.FromMilliseconds(50),
                    MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                    VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                    ReadyTimeout = TimeSpan.FromSeconds(5),
                }
            );
            removalExtension.AddNode(removalNode);
            await removalExtension.ConnectAllNodesAsync().ConfigureAwait(false);
            AcceptPlayerPatch(removalServer);
            var connect = removalNode.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            removalBridge.FireGuildRemoved(777);
            await Assert
                .That(async () =>
                {
                    await connect.ConfigureAwait(false);
                })
                .Throws<VoiceHandshakeException>();
            await removalExtension.DisposeAsync().ConfigureAwait(false);
            removalServer.Dispose();
        }
    }

    private sealed class RemovalBridge : Nyra.Abstractions.IDiscordBridge, IGuildRemovalSource
    {
        private readonly TaskCompletionSource<Nyra.Abstractions.ClientInformation> _ready = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public event EventHandler<Nyra.Abstractions.VoiceServerUpdatedEventArgs>? VoiceServerUpdated
        {
            add { }
            remove { }
        }

        public event EventHandler<Nyra.Abstractions.VoiceStateUpdatedEventArgs>? VoiceStateUpdated
        {
            add { }
            remove { }
        }

        public event EventHandler<GuildRemovedEventArgs>? GuildRemoved;

        public Microsoft.Extensions.Logging.ILogger? Logger { get; set; }

        public RecordingList<(ulong GuildId, ulong? ChannelId)> VoiceSends { get; } = new();

        public void GoReady() =>
            _ready.TrySetResult(new Nyra.Abstractions.ClientInformation(FakeBridge.UserId));

        public Task<Nyra.Abstractions.ClientInformation> WaitForReadyAsync(
            CancellationToken cancellationToken = default
        ) => _ready.Task.WaitAsync(cancellationToken);

        public Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        )
        {
            VoiceSends.Add((guildId, channelId));
            return Task.CompletedTask;
        }

        public void FireGuildRemoved(ulong guildId) =>
            GuildRemoved?.Invoke(this, new GuildRemovedEventArgs(guildId));
    }
}
