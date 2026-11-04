using System.Net;
using System.Net.Sockets;

namespace Nyra.Tests;

public sealed class NodeConnectEdge
{
    private sealed record Stack : IAsyncDisposable
    {
        public required NyraExtension Extension { get; init; }

        public required Node Node { get; init; }

        public required FakeNodeServer Server { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

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
                Name = "connect-edges",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new Stack
        {
            Extension = extension,
            Node = node,
            Server = server,
        };
    }

    [Test]
    public async Task StopOverASilentHandshake()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);

            server.GhostHandshakes = true;
            await server.DropConnectionAsync(1000, "rotation").ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
            await Assert.That(node.IsReady).IsFalse();

            var stopped = node.DisconnectAsync();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await stopped.WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            sw.Stop();
            await Assert.That(node.IsReady).IsFalse();
            await Assert.That(sw.Elapsed).IsLessThan(TimeSpan.FromSeconds(4));
        }
    }

    [Test]
    public async Task ASecondConnectFailsLikeTheFirstOne()
    {
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", NodeHarness.DeadPort(), "unused"),
            new NodeOptions
            {
                Name = "second-connect",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
            }
        );
        extension.AddNode(node);

        var first = await Assert
            .That(async () =>
                await node.ConnectAsync(bridge, extension, CancellationToken.None)
                    .ConfigureAwait(false)
            )
            .Throws<Exception>();
        var second = await Assert
            .That(async () =>
                await node.ConnectAsync(bridge, extension, CancellationToken.None)
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false)
            )
            .Throws<Exception>();
        await Assert.That(second).IsSameReferenceAs(first);

        await extension
            .ConnectAllNodesAsync()
            .WaitAsync(TimeSpan.FromSeconds(5))
            .ConfigureAwait(false);

        await extension.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task AReplayedConnectFailureKeepsItsStackBounded()
    {
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", NodeHarness.DeadPort(), "unused"),
            new NodeOptions
            {
                Name = "replay-stack",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
            }
        );
        extension.AddNode(node);

        var first = await Assert
            .That(async () =>
                await node.ConnectAsync(bridge, extension, CancellationToken.None)
                    .ConfigureAwait(false)
            )
            .Throws<Exception>();
        for (var replay = 0; replay < 2; replay++)
            await Assert
                .That(async () =>
                    await node.ConnectAsync(bridge, extension, CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false)
                )
                .Throws<Exception>();
        var settled = first!.StackTrace!.Length;

        for (var replay = 0; replay < 200; replay++)
            await Assert
                .That(async () =>
                    await node.ConnectAsync(bridge, extension, CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false)
                )
                .Throws<Exception>();

        await Assert.That(first.StackTrace!.Length).IsLessThan(settled * 2);

        await extension.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task ACancelledConnectIsNotReplayedToTheNextOne()
    {
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var node = new Node(
            new Endpoint("127.0.0.1", ((IPEndPoint)silent.LocalEndpoint).Port, "pass"),
            new NodeOptions
            {
                Name = "cancelled-then-again",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(8),
            }
        );
        extension.AddNode(node);

        using var cancelled = new CancellationTokenSource(150);
        await Assert
            .That(async () =>
                await node.ConnectAsync(bridge, extension, cancelled.Token).ConfigureAwait(false)
            )
            .Throws<OperationCanceledException>();

        var next = node.ConnectAsync(bridge, extension, CancellationToken.None);
        await Assert
            .That(async () =>
                await next.WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false)
            )
            .Throws<TimeoutException>();

        await extension.DisposeAsync().ConfigureAwait(false);
        await next.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    [Test]
    public async Task DiscardAfterStop()
    {
        var server = new FakeNodeServer { HandshakeDelay = TimeSpan.FromMilliseconds(1500) };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "discard-tail",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);

        var connect = Task.Run(async () =>
        {
            try
            {
                await extension.ConnectAllNodesAsync().ConfigureAwait(false);
            }
            catch { }
        });
        await Task.Delay(400).ConfigureAwait(false);
        await node.DisconnectAsync().ConfigureAwait(false);
        await Assert.That(node.IsReady).IsFalse();

        server.HandshakeDelay = TimeSpan.Zero;
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
        await connect.ConfigureAwait(false);
    }
}
