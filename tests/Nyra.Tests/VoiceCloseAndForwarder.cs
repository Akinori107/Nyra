using System.Collections.Concurrent;
using DSharpPlus;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using Nyra.Abstractions;
using Nyra.DSharpPlus;
using Nyra.Exceptions;
using Nyra.Internal;
using Nyra.NetCord;
using static Nyra.Tests.NodeHarness;
using DspBridge = Nyra.DSharpPlus.DSharpPlusBridge;

namespace Nyra.Tests;

public sealed class VoiceCloseAndForwarder
{
    private sealed class HostileLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public RecordingList<string> Messages { get; } = new();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Messages.Add(formatter(state, exception));
            throw new InvalidOperationException("the logger bit back");
        }
    }

    private static async Task<SupervisionLoopRace.Stack> StartAsync(string name = "voice-close")
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = name,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(250),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new SupervisionLoopRace.Stack
        {
            Extension = extension,
            Node = node,
            Bridge = bridge,
            Server = server,
        };
    }

    [Test]
    public async Task HandshakeSkipWindowEcho()
    {
        var bridge = new FakeBridge();
        var coordinator = new VoiceHandshakeCoordinator(TimeSpan.FromSeconds(5));
        await coordinator
            .LeaveChannelAsync(bridge, 777)
            .WaitAsync(TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);

        var echo = new VoiceStateUpdatedEventArgs(777, new DiscordVoiceState(null, "vsess"));
        var first = coordinator.HandleVoiceStateUpdate(bridge, echo);
        await coordinator
            .LeaveChannelAsync(bridge, 777)
            .WaitAsync(TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);
        var second = coordinator.HandleVoiceStateUpdate(bridge, echo);

        await Assert.That(first).IsTrue();
        await Assert.That(second).IsTrue();
    }

    [Test]
    public async Task HandshakePoisonedState()
    {
        var bridge = new FakeBridge();
        var coordinator = new VoiceHandshakeCoordinator(TimeSpan.FromSeconds(5));
        var wait = coordinator.WaitForUpdatesAsync(888, 555, bridge, CancellationToken.None);
        await Task.Delay(150).ConfigureAwait(false);

        var pending = (
            (System.Collections.IDictionary)Reflect.FieldValue(coordinator, "_pending")!
        )[888UL]!;
        var state =
            (TaskCompletionSource<DiscordVoiceState>)
                Reflect.FieldValue(pending, "<State>k__BackingField")!;
        var server =
            (TaskCompletionSource<VoiceServer>)
                Reflect.FieldValue(pending, "<Server>k__BackingField")!;
        state.TrySetException(new InvalidOperationException("poisoned state"));
        server.TrySetException(new InvalidOperationException("poisoned state"));

        await Assert
            .That(async () => await wait.ConfigureAwait(false))
            .Throws<VoiceHandshakeException>()
            .WithMessageContaining("poisoned state");
    }

    [Test]
    public async Task VoiceCloseReentry()
    {
        var stack = await StartAsync("close-reentry").ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            bridge.HangVoiceSends = true;

            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":1000,"reason":"first","byRemote":true}"""
                )
                .ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":1000,"reason":"second","byRemote":true}"""
                )
                .ConfigureAwait(false);
            await Task.Delay(3000).ConfigureAwait(false);

            var rescueSends = bridge.VoiceSends.Count(s => s.ChannelId == 555);
            await Assert.That(rescueSends).IsEqualTo(1);
            bridge.HangVoiceSends = false;
            await Task.Delay(10_500).ConfigureAwait(false);
            _ = conn;
        }
    }

    [Test]
    public async Task VoiceCloseSecondRescueSettles()
    {
        var stack = await StartAsync("close-settle").ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":1000,"reason":"settle","byRemote":true}"""
                )
                .ConfigureAwait(false);

            await Task.Delay(4600).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess2");
            await Task.Delay(5200).ConfigureAwait(false);

            var kept = node.GetConnection(777);
            await Assert.That(kept).IsNotNull();
            await Assert.That(kept!.ChannelId).IsEqualTo(555UL);
        }
    }

    [Test]
    public async Task HandlerErrorHostileLogger()
    {
        var stack = await StartAsync("handler-error").ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            var logger = new HostileLogger();
            bridge.Logger = logger;

            var invalidatedCalls = 0;
            var handlerErrors = 0;
            conn.GuildConnectionInvalidated += (_, _) =>
            {
                Interlocked.Increment(ref invalidatedCalls);
                throw new InvalidOperationException("no");
            };
            conn.HandlerError += (_, _) =>
            {
                Interlocked.Increment(ref handlerErrors);
                throw new InvalidOperationException("nope");
            };

            await conn.Teardown.HandleSessionInvalidatedAsync().ConfigureAwait(false);
            await conn.CompleteEventPumpAsync().ConfigureAwait(false);

            await Assert.That(Volatile.Read(ref invalidatedCalls)).IsEqualTo(1);
            await Assert.That(Volatile.Read(ref handlerErrors)).IsEqualTo(1);
            bridge.Logger = null;
        }
    }

    [Test]
    public async Task WiringNullChannelHostileLogger()
    {
        var stack = await StartAsync("null-channel").ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            var logger = new HostileLogger();
            bridge.Logger = logger;

            bridge.FireVoiceState(777, null, "vsess");
            await Assert
                .That(() => node.GetConnection(777) is null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            bridge.Logger = null;
        }
    }

    [Test]
    public async Task VoiceStateMovesOnAnotherChannelAndRefreshesOnTheSame()
    {
        var stack = await StartAsync("wiring-move").ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            bridge.FireVoiceState(777, 999, "moved-session");
            await Assert
                .That(() => conn.ChannelId == 999)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Assert.That(conn.VoiceSessionId).IsEqualTo("moved-session");

            bridge.FireVoiceState(777, 999, "rotated-session");
            await Assert
                .That(() => conn.VoiceSessionId == "rotated-session")
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Assert.That(conn.ChannelId).IsEqualTo(999UL);
        }
    }

    [Test]
    public async Task EventPumpOverflowHostile()
    {
        var pump = new EventPump("voice-close", new HostileLogger(), default);
        var park = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pump.TryDispatch(() => park.Task);

        var dropped = 0;
        for (var i = 0; i < 700; i++)
        {
            if (!pump.TryDispatch(() => Task.CompletedTask, droppable: true))
                dropped++;
        }

        park.TrySetResult();
        await Assert.That(dropped).IsGreaterThan(0);
    }

    [Test]
    public async Task DspStableGatewayStillConnecting()
    {
        using var client = Adapter.NewOfflineClient(51);
        using var bridge = new DspBridge(client);
        var sockets =
            (ConcurrentDictionary<DiscordClient, byte>)
                Reflect.FieldValue(bridge, "_gatewaySocketsUp")!;
        sockets.Clear();

        await Assert
            .That(async () =>
                await bridge
                    .SendVoiceUpdateAsync(5, 77, false, false, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(3))
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RemoveNyraAsyncDisposes()
    {
        var client = Adapter.NewOfflineClient(53);
        using var bridgeHolder = client.UseNyra();
        await client.RemoveNyraAsync().ConfigureAwait(false);
        var second = client.UseNyra();
        await Assert.That(ReferenceEquals(bridgeHolder, second)).IsFalse();
        second.Dispose();
        client.Dispose();
    }

    [Test]
    public async Task NetCordChannelOverload()
    {
        var stack = await StartAsync("nc-overload").ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var channel = new global::NetCord.VoiceGuildChannel(
                new global::NetCord.JsonModels.JsonChannel { Id = 77 },
                777,
                null!
            );
            AcceptPlayerPatch(stack.Server, 777);
            var connect = stack.Node.ConnectAsync(channel, CancellationToken.None);
            await Assert
                .That(() => stack.Bridge.VoiceSends.Count)
                .WaitsFor(assert => assert.IsGreaterThan(0), TimeSpan.FromSeconds(5));
            stack.Bridge.FireVoiceState(777, 77, "vsess");
            stack.Bridge.FireVoiceServer(777, "tok", "127.0.0.1");
            var forwarded = await connect.WaitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false);

            await Assert.That(forwarded.GuildId).IsEqualTo(777UL);
            await Assert.That(forwarded.ChannelId).IsEqualTo(77UL);
        }
    }

    [Test]
    public async Task NetCordShardWarningHostile()
    {
        var token = new BotToken("MTIz.ab.cd");
        var sharded = new ShardedGatewayClient(token);
        var shard = new GatewayClient(
            token,
            new GatewayClientConfiguration { Shard = new Shard(0, 1) }
        );
        InjectShards(sharded, shard);

        var shardOwn = shard.UseNyra();
        var shardedExtension = sharded.UseNyra(new HostileLogger());

        await Assert.That(shardedExtension).IsNotNull();
        await Assert.That(shardedExtension!.IsDisposed).IsFalse();

        shardedExtension?.Dispose();
        shardOwn?.Dispose();
        sharded?.Dispose();
        shard?.Dispose();
    }

    private sealed class ProbeBridge : GatewayBridgeBase
    {
        private readonly GatewayClient _client;

        public ProbeBridge(GatewayClient client)
            : base(_ => { }, _ => { }, _ => { }) => _client = client;

        public RecordingList<ulong> ResolvedGuilds { get; } = new();

        protected override GatewayClient GetClient(ulong guildId)
        {
            ResolvedGuilds.Add(guildId);
            return _client;
        }

        protected override void Unsubscribe() { }
    }

    [Test]
    public async Task NetCordBridgeSendVoice()
    {
        var token = new BotToken("MTIz.ab.cd");
        var client = new GatewayClient(
            token,
            new GatewayClientConfiguration { Shard = new Shard(0, 1) }
        );
        ForceReady(client);

        using var bridge = new ProbeBridge(client);
        var send = bridge.SendVoiceUpdateAsync(5, 77, false, false, CancellationToken.None);
        await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);

        await Assert.That(send.IsCompleted).IsTrue();
        await Assert.That(bridge.ResolvedGuilds.Snapshot()).IsEquivalentTo([5UL]);
        _ = send.Exception;
        client.Dispose();
    }
}
