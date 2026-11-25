using Nyra.Abstractions;
using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class VoiceHandshakeEdge
{
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

    private sealed class HeldJoinBridge : FakeBridge, IGuildRemovalSource
    {
        public event EventHandler<GuildRemovedEventArgs>? GuildRemoved;

        public void FireGuildRemoved(ulong guildId) =>
            GuildRemoved?.Invoke(this, new GuildRemovedEventArgs(guildId));

        public TaskCompletionSource JoinSent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource JoinMayFinish { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        )
        {
            await base.SendVoiceUpdateAsync(
                    guildId,
                    channelId,
                    selfDeaf,
                    selfMute,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (channelId is not null)
            {
                JoinSent.TrySetResult();
                await JoinMayFinish.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [Test]
    public async Task GuildRemovalAfterBothVoiceUpdatesPreventsPlayerRegistration()
    {
        var bridge = new HeldJoinBridge();
        var stack = await StartAsync(bridge: bridge).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            AcceptPlayerPatch(stack.Server);
            var join = stack.Node.ConnectAsync(777, 555);
            try
            {
                await bridge.JoinSent.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                bridge.FireVoiceState(777, 555, "vsess");
                bridge.FireVoiceServer(777, "tok", "127.0.0.1");
                bridge.FireGuildRemoved(777);
            }
            finally
            {
                bridge.JoinMayFinish.TrySetResult();
            }

            await Assert
                .That(async () =>
                {
                    await join.ConfigureAwait(false);
                })
                .Throws<VoiceHandshakeException>();
            await Assert.That(stack.Node.GetConnection(777)).IsNull();
            await Assert
                .That(
                    stack.Server.RestCalls.Any(call =>
                        call.Method == "PATCH" && call.Path == PlayerPath
                    )
                )
                .IsFalse();
        }
    }

    [Test]
    public async Task FailedRegistrationInvalidatesAConnectionAlreadyReadByTheApp()
    {
        var bridge = new HeldJoinBridge();
        var stack = await StartAsync(bridge: bridge).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var registering = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var reject = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            stack.Server.On("PATCH", PlayerPath, _ =>
            {
                registering.TrySetResult();
                reject.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                return new FakeRestResponse(400, """{"message":"registration rejected"}""");
            });

            var join = stack.Node.ConnectAsync(777, 555);
            GuildConnection? published;
            try
            {
                await bridge.JoinSent.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                bridge.FireVoiceState(777, 555, "vsess");
                bridge.FireVoiceServer(777, "tok", "127.0.0.1");
                bridge.JoinMayFinish.TrySetResult();
                await registering.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                published = stack.Node.GetConnection(777);
            }
            finally
            {
                bridge.JoinMayFinish.TrySetResult();
                reject.TrySetResult();
            }

            await Assert
                .That(async () =>
                {
                    await join.ConfigureAwait(false);
                })
                .Throws<RestException>();
            await Assert.That(published).IsNotNull();
            await Assert.That(published!.IsConnected).IsFalse();
            await Assert.That(() => published.PauseAsync()).Throws<InvalidOperationException>();
            await Assert.That(stack.Node.GetConnection(777)).IsNull();
        }
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
            await WaitForJoinAsync(bridge).ConfigureAwait(false);
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
            var node = stack.Node;
            var server = stack.Server;
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
}
