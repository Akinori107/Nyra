namespace Nyra.Tests;

public sealed class QuickReconnect
{
    private sealed record Stack : IAsyncDisposable
    {
        public required FakeBridge Bridge { get; init; }

        public required NyraExtension Extension { get; init; }

        public required Node First { get; init; }

        public required Node Second { get; init; }

        public required FakeNodeServer FirstServer { get; init; }

        public required FakeNodeServer SecondServer { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            FirstServer.Dispose();
            SecondServer.Dispose();
        }
    }

    private static void AcceptPlayerPatch(FakeNodeServer server, string session = "fake-session") =>
        server.On(
            "PATCH",
            $"/v4/sessions/{session}/players/777",
            _ => new FakeRestResponse(
                200,
                """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
            )
        );

    private static async Task<Stack> StartAsync()
    {
        var firstServer = new FakeNodeServer();
        var secondServer = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var first = new Node(
            new Endpoint("127.0.0.1", firstServer.Port, firstServer.Password),
            new NodeOptions
            {
                Name = "first",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        var second = new Node(
            new Endpoint("127.0.0.1", secondServer.Port, secondServer.Password),
            new NodeOptions
            {
                Name = "second",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(first);
        extension.AddNode(second);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new Stack
        {
            Bridge = bridge,
            Extension = extension,
            First = first,
            Second = second,
            FirstServer = firstServer,
            SecondServer = secondServer,
        };
    }

    private static async Task<GuildConnection> ConnectGuildAsync(
        FakeBridge bridge,
        Node node,
        FakeNodeServer server,
        ulong channel = 555,
        string session = "fake-session"
    )
    {
        AcceptPlayerPatch(server, session);
        var connect = node.ConnectAsync(777, channel);
        await Task.Delay(150).ConfigureAwait(false);
        bridge.FireVoiceState(777, channel, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        return await connect.ConfigureAwait(false);
    }

    [Test]
    public async Task SameNodeQuickFlipLeaveOrder()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, first) = (stack.Bridge, stack.First);

            var e1 = await ConnectGuildAsync(bridge, first, stack.FirstServer, 555)
                .ConfigureAwait(false);
            bridge.VoiceSends.Clear();

            var disconnect = e1.DisconnectAsync();
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, null, "vsess");
            var e2 = await ConnectGuildAsync(bridge, first, stack.FirstServer, 556)
                .ConfigureAwait(false);
            await disconnect.ConfigureAwait(false);

            await Assert.That(first.GetConnection(777) is { }).IsTrue();
            await Assert.That(first.GetConnection(777)!.ChannelId).IsEqualTo(556);

            var sends = bridge.VoiceSends;
            var leaveIndex = sends.FindIndex(s => s.ChannelId is null);
            var joinIndex = sends.FindIndex(s => s.ChannelId == 556);
            await Assert
                .That(leaveIndex == -1 || joinIndex == -1 || leaveIndex < joinIndex)
                .IsTrue();

            await e2.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RacingDisconnectAndConnect()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, first, second) = (stack.Bridge, stack.First, stack.Second);

            var e1 = await ConnectGuildAsync(bridge, first, stack.FirstServer, 555)
                .ConfigureAwait(false);
            bridge.VoiceSends.Clear();

            var disconnect = e1.DisconnectAsync();
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, null, "vsess");
            var connectTask = Task.Run(async () =>
            {
                AcceptPlayerPatch(stack.SecondServer);
                var connect = second.ConnectAsync(777, 557);
                await Task.Delay(150).ConfigureAwait(false);
                bridge.FireVoiceState(777, 557, "vsess");
                bridge.FireVoiceServer(777, "tok", "127.0.0.1");
                return await connect.ConfigureAwait(false);
            });
            var e2 = await connectTask.ConfigureAwait(false);
            await disconnect.ConfigureAwait(false);

            await Assert.That(second.GetConnection(777) is { }).IsTrue();
            await Assert.That(second.GetConnection(777)!.ChannelId).IsEqualTo(557);

            var sends = bridge.VoiceSends;
            var lastJoin = sends.FindLastIndex(s => s.ChannelId is not null);
            var lastLeave = sends.FindLastIndex(s => s.ChannelId is null);
            await Assert.That(lastLeave).IsLessThan(lastJoin);

            var lateEchoKilled = false;
            bridge.FireVoiceState(777, null, "vsess");
            await Task.Delay(200).ConfigureAwait(false);
            lateEchoKilled = second.GetConnection(777) is null;
            await Assert.That(lateEchoKilled).IsTrue();

            var e2Final = second.GetConnection(777);
            if (e2Final is { })
                await e2Final.DisconnectAsync().ConfigureAwait(false);
        }
    }
}
