using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class VoiceClosePath
{
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
                Name = "close-paths",
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
    public async Task SuppressedCloseIsAChannelSwitch()
    {
        var server = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
        var stack = await StartAsync(server).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, sv) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, sv).ConfigureAwait(false);
            await Assert.That(node.IsNodeLink).IsFalse();

            await sv.PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":4006,"reason":"switch","byRemote":true}"""
                )
                .ConfigureAwait(false);
            bridge.FireVoiceServer(777, "tok2", "127.0.0.1");

            await Task.Delay(3200).ConfigureAwait(false);
            await Assert.That(node.GetConnection(777)).IsNotNull();
            await Assert.That(conn.IsConnected).IsTrue();
            await Assert.That(bridge.VoiceSends.Any(s => s.ChannelId is null)).IsFalse();

            await conn.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task TornDownDuringGrace()
    {
        var server = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
        var stack = await StartAsync(server).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, sv) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, sv).ConfigureAwait(false);

            await sv.PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":4006,"reason":"boom","byRemote":true}"""
                )
                .ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await conn.DisconnectAsync().ConfigureAwait(false);
            await Assert.That(node.GetConnection(777)).IsNull();
            await Task.Delay(2500).ConfigureAwait(false);
            await Assert.That(node.GetConnection(777)).IsNull();
            await Assert.That(conn.IsConnected).IsFalse();
        }
    }

    private static void FailPlayerPatch(FakeNodeServer server) =>
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(500, "{}"));

    [Test]
    public async Task RescueAnsweredByFreshForward()
    {
        var server = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
        var stack = await StartAsync(server).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, sv) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, sv).ConfigureAwait(false);

            var forwardFailed = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            node.Error += (_, _) =>
            {
                forwardFailed.TrySetResult(true);
                return Task.CompletedTask;
            };

            FailPlayerPatch(sv);
            bridge.FireVoiceServer(777, "tok2", "127.0.0.1");
            await Assert
                .That(
                    await forwardFailed
                        .Task.WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false)
                )
                .IsTrue();

            await sv.PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":4009,"reason":"late","byRemote":true}"""
                )
                .ConfigureAwait(false);

            var sendsBeforeClose = bridge.VoiceSends.Count;
            var deadline = Environment.TickCount64 + 4000;
            var rejoinSent = false;
            while (Environment.TickCount64 < deadline && !rejoinSent)
            {
                await Task.Delay(100).ConfigureAwait(false);
                rejoinSent = bridge.VoiceSends.Count > sendsBeforeClose;
            }
            await Assert.That(rejoinSent).IsTrue();

            AcceptPlayerPatch(sv);
            bridge.FireVoiceState(777, 555, "vsess2");
            bridge.FireVoiceServer(777, "tok3", "127.0.0.1");

            await Task.Delay(6500).ConfigureAwait(false);
            await Assert.That(node.GetConnection(777)).IsNotNull();
            await Assert.That(conn.IsConnected).IsTrue();

            await conn.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RescueThroughADeadGateway()
    {
        var server = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
        var stack = await StartAsync(server).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, sv) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, sv).ConfigureAwait(false);

            var forwardFailed = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            node.Error += (_, _) =>
            {
                forwardFailed.TrySetResult(true);
                return Task.CompletedTask;
            };
            FailPlayerPatch(sv);
            bridge.FireVoiceServer(777, "tok2", "127.0.0.1");
            await forwardFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            bridge.FaultVoiceSends = true;
            await sv.PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":4009,"reason":"dead","byRemote":true}"""
                )
                .ConfigureAwait(false);

            var removed = false;
            for (var i = 0; i < 140 && !removed; i++)
            {
                await Task.Delay(100).ConfigureAwait(false);
                removed = node.GetConnection(777) is null;
            }
            await Assert.That(removed).IsTrue();
            await Assert.That(conn.IsConnected).IsFalse();
        }
    }

    [Test]
    public async Task TornDownDuringRescueWindow()
    {
        var server = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
        var stack = await StartAsync(server).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, sv) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, sv).ConfigureAwait(false);

            var forwardFailed = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            node.Error += (_, _) =>
            {
                forwardFailed.TrySetResult(true);
                return Task.CompletedTask;
            };
            FailPlayerPatch(sv);
            bridge.FireVoiceServer(777, "tok2", "127.0.0.1");
            await forwardFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            await sv.PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":4009,"reason":"window","byRemote":true}"""
                )
                .ConfigureAwait(false);

            await Task.Delay(2600).ConfigureAwait(false);
            await Assert.That(bridge.VoiceSends).Contains(s => s.ChannelId == 555);
            await conn.DisconnectAsync().ConfigureAwait(false);
            await Assert.That(node.GetConnection(777)).IsNull();
            await Task.Delay(5500).ConfigureAwait(false);
            await Assert.That(node.GetConnection(777)).IsNull();
            await Assert.That(conn.IsConnected).IsFalse();
        }
    }

    [Test]
    public async Task InvalidationRaiseDetachesWhenPumpIsGone()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            var raised = new TaskCompletionSource<ulong>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            conn.GuildConnectionInvalidated += (_, args) =>
            {
                raised.TrySetResult(args.GuildId);
                return Task.CompletedTask;
            };

            await conn.CompleteEventPumpAsync().ConfigureAwait(false);
            await conn.Teardown.HandleSessionInvalidatedAsync().ConfigureAwait(false);

            await Assert
                .That(await raised.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsEqualTo(777);
            await Assert.That(node.GetConnection(777)).IsNull();
        }
    }
}
