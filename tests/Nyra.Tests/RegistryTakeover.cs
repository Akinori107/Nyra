using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class RegistryTakeover
{
    private static async Task<Stack> StartAsync(FakeNodeServer? server = null)
    {
        server ??= new FakeNodeServer();
        server.On("PATCH", "/v4/sessions/fake-session", 200, """{"resuming":true,"timeout":60}""");
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "registry",
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
    public async Task ConnectGuards()
    {
        var orphan = new Node(
            new Endpoint("127.0.0.1", 1, "pass"),
            new NodeOptions { Name = "orphan" }
        );
        await Assert
            .That(async () =>
            {
                await orphan.ConnectAsync(777, 555).ConfigureAwait(false);
            })
            .Throws<InvalidOperationException>();

        var deadServer = new FakeNodeServer();
        var port = deadServer.Port;
        deadServer.Dispose();
        var idleBridge = new FakeBridge();
        idleBridge.GoReady();
        var idleExtension = new NyraExtension(idleBridge);
        var idleNode = new Node(
            new Endpoint("127.0.0.1", port, "pass"),
            new NodeOptions
            {
                Name = "idle",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromMilliseconds(500),
            }
        );
        idleExtension.AddNode(idleNode);
        try
        {
            await idleExtension.ConnectAllNodesAsync().ConfigureAwait(false);
        }
        catch { }
        await Assert
            .That(async () =>
            {
                await idleNode.ConnectAsync(777, 555).ConfigureAwait(false);
            })
            .Throws<InvalidOperationException>();
        await idleExtension.DisposeAsync().ConfigureAwait(false);

        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            AcceptPlayerPatch(stack.Server);
            await stack.Extension.DisposeAsync().ConfigureAwait(false);
            await Assert
                .That(async () =>
                {
                    await stack.Node.ConnectAsync(777, 555).ConfigureAwait(false);
                })
                .Throws<ObjectDisposedException>();
            stack.Server.Dispose();
        }
    }

    [Test]
    public async Task SerializedGate()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);

            var parked = new TaskCompletionSource();
            var release = new TaskCompletionSource();
            server.On(
                "PATCH",
                PlayerPath,
                _ =>
                {
                    parked.TrySetResult();
                    release.Task.Wait(TimeSpan.FromSeconds(10));
                    return new FakeRestResponse(
                        200,
                        """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
                    );
                }
            );

            var first = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess");
            bridge.FireVoiceServer(777, "tok", "127.0.0.1");
            await parked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert
                .That(async () =>
                {
                    await node.ConnectAsync(777, 556, cancelled.Token).ConfigureAwait(false);
                })
                .Throws<OperationCanceledException>();

            var second = node.ConnectAsync(777, 555);
            release.TrySetResult();
            var connectedFirst = await first.ConfigureAwait(false);
            var connectedSecond = await second.ConfigureAwait(false);
            await Assert.That(ReferenceEquals(connectedFirst, connectedSecond)).IsTrue();

            await connectedSecond.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task TeardownTakeover()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var retiring = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await Assert.That(retiring.ChannelId).IsEqualTo(555);

            var retiredSawUpdate = false;
            retiring.VolumeChanged += (_, _) =>
            {
                retiredSawUpdate = true;
                return Task.CompletedTask;
            };

            retiring.MarkTearingDown();
            var successorTask = node.ConnectAsync(777, 556);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 556, "vsess2");
            bridge.FireVoiceServer(777, "tok2", "127.0.0.1");
            var successor = await successorTask.ConfigureAwait(false);
            await Assert.That(successor.ChannelId).IsEqualTo(556);
            await Assert.That(successor.GuildId).IsEqualTo(777);
            await Assert.That(ReferenceEquals(successor, retiring)).IsFalse();

            var successorSawUpdate = new TaskCompletionSource<int>();
            successor.VolumeChanged += (_, args) =>
            {
                successorSawUpdate.TrySetResult(args.Volume);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"VolumeChangedEvent","volume":42}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    await successorSawUpdate
                        .Task.WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false)
                )
                .IsEqualTo(42);
            await Task.Delay(200).ConfigureAwait(false);
            await Assert.That(retiredSawUpdate).IsFalse();

            await successor.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ConnectRollbacks()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            server.On(
                "PATCH",
                PlayerPath,
                _ => new FakeRestResponse(500, "{\"message\":\"boom\"}")
            );
            var rejected = false;
            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess");
            bridge.FireVoiceServer(777, "tok", "127.0.0.1");
            try
            {
                await connect.ConfigureAwait(false);
            }
            catch (RestException)
            {
                rejected = true;
            }
            await Assert.That(rejected).IsTrue();
            await Task.Delay(100).ConfigureAwait(false);
            await Assert.That(node.GetConnection(777)).IsNull();
            await Assert.That(bridge.VoiceSends.Contains((777, null))).IsTrue();
        }

        var renewal = await StartAsync().ConfigureAwait(false);
        await using (renewal.ConfigureAwait(false))
        {
            var (bridge, node, server) = (renewal.Bridge, renewal.Node, renewal.Server);
            var parked = new TaskCompletionSource();
            var release = new TaskCompletionSource();
            server.On(
                "PATCH",
                PlayerPath,
                _ =>
                {
                    parked.TrySetResult();
                    release.Task.Wait(TimeSpan.FromSeconds(10));
                    return new FakeRestResponse(
                        200,
                        """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
                    );
                }
            );
            server.On("DELETE", PlayerPath, _ => new FakeRestResponse(204, string.Empty));
            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, "vsess");
            bridge.FireVoiceServer(777, "tok", "127.0.0.1");
            await parked.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            node.GetConnection(777)?.MarkTearingDown();
            release.TrySetResult();
            await Assert
                .That(async () =>
                {
                    await connect.ConfigureAwait(false);
                })
                .Throws<InvalidOperationException>();
            await Task.Delay(100).ConfigureAwait(false);
            await Assert.That(node.GetConnection(777)).IsNull();
            await Assert
                .That(server.RestCalls)
                .Contains(c => c is { Method: "DELETE", Path: PlayerPath });
        }

        var renewal2 = await StartAsync().ConfigureAwait(false);
        await using (renewal2.ConfigureAwait(false))
        {
            var secondServer = new FakeNodeServer();
            var bridge = renewal2.Bridge;
            var extension = renewal2.Extension;
            var second = new Node(
                new Endpoint("127.0.0.1", secondServer.Port, secondServer.Password),
                new NodeOptions
                {
                    Name = "second",
                    ReconnectDelay = TimeSpan.FromMilliseconds(50),
                    MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                    VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                    ReadyTimeout = TimeSpan.FromSeconds(5),
                }
            );
            extension.AddNode(second);
            await extension.ConnectAllNodesAsync().ConfigureAwait(false);
            var owner = await ConnectGuildAsync(bridge, renewal2.Node, renewal2.Server)
                .ConfigureAwait(false);

            var denied = false;
            var secondConnect = second.ConnectAsync(777, 556);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 556, "vsess3");
            bridge.FireVoiceServer(777, "tok3", "127.0.0.1");
            try
            {
                await secondConnect.ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                denied = true;
            }
            await Assert.That(denied).IsTrue();
            await owner.DisconnectAsync().ConfigureAwait(false);
            secondServer.Dispose();
        }

        var renewal3 = await StartAsync().ConfigureAwait(false);
        await using (renewal3.ConfigureAwait(false))
        {
            var (bridge, node, server) = (renewal3.Bridge, renewal3.Node, renewal3.Server);
            AcceptPlayerPatch(server);
            var noSession = false;
            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            bridge.FireVoiceState(777, 555, string.Empty);
            bridge.FireVoiceServer(777, "tok", "127.0.0.1");
            try
            {
                await connect.ConfigureAwait(false);
            }
            catch (VoiceHandshakeException)
            {
                noSession = true;
            }
            await Assert.That(noSession).IsTrue();
        }
    }
}
