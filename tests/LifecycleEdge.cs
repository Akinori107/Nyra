using System.Collections.Concurrent;
using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class LifecycleEdge
{
    private const string GhostPath = "/v4/sessions/fake-session/players/888";

    private static async Task<Stack> StartAsync(
        FakeNodeServer? server = null,
        TimeProvider? time = null
    )
    {
        server ??= new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge, time);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "lifecycle-edge",
                TimeProvider = time ?? TimeProvider.System,
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new Stack
        {
            Extension = extension,
            Node = node,
            Bridge = bridge,
            Server = server,
        };
    }

    private sealed class SyncDisposeBridge : FakeBridge, IDisposable
    {
        public bool Disposed;

        public void Dispose() => Disposed = true;
    }

    private sealed class ThrowingSyncBridge : FakeBridge, IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("sync dispose boom");
    }

    [Test]
    public async Task BridgeDispose()
    {
        var server = new FakeNodeServer();
        var bridge = new SyncDisposeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions { Name = "sync-dispose", ReadyTimeout = TimeSpan.FromSeconds(5) }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        extension.Dispose();
        await Assert.That(bridge.Disposed).IsTrue();
        server.Dispose();

        var server2 = new FakeNodeServer();
        var bridge2 = new ThrowingSyncBridge();
        bridge2.GoReady();
        var extension2 = new NyraExtension(bridge2);
        var node2 = new Node(
            new Endpoint("127.0.0.1", server2.Port, server2.Password),
            new NodeOptions { Name = "throwing-dispose", ReadyTimeout = TimeSpan.FromSeconds(5) }
        );
        extension2.AddNode(node2);
        await extension2.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert
            .That(extension2.Dispose)
            .Throws<InvalidOperationException>()
            .WithMessageContaining("sync dispose boom");
        server2.Dispose();
    }

    [Test]
    public async Task OrphanSweep()
    {
        var destroyed = new ConcurrentQueue<string>();
        var mode = 0;
        var stack = await StartAsync().ConfigureAwait(false);
        try
        {
            await ConnectGuildAsync(stack.Bridge, stack.Node, stack.Server).ConfigureAwait(false);
            stack.Server.On("GET", "/v4/sessions/fake-session/players", _ => new FakeRestResponse(
                200,
                """[{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}},{"guildId":"888","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}]"""
            ));
            stack.Server.On("DELETE", GhostPath, _ =>
            {
                destroyed.Enqueue("888");
                return mode switch
                {
                    1 => new FakeRestResponse(404, string.Empty),
                    2 => new FakeRestResponse(500, "destroy exploded"),
                    _ => new FakeRestResponse(204, string.Empty),
                };
            });

            await stack.Server.DropConnectionAsync(1000, "orphan test drop").ConfigureAwait(false);
            var resumedAgain = new TaskCompletionSource<bool>();
            stack.Node.Connected += (_, _) =>
            {
                resumedAgain.TrySetResult(true);
                return Task.CompletedTask;
            };
            await resumedAgain.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            await Assert
                .That(() => destroyed.Count)
                .WaitsFor(assert => assert.IsEqualTo(1), TimeSpan.FromSeconds(4));

            mode = 1;
            await stack.Server.DropConnectionAsync(1000, "orphan 404 drop").ConfigureAwait(false);
            await Assert
                .That(() => destroyed.Count)
                .WaitsFor(assert => assert.IsEqualTo(2), TimeSpan.FromSeconds(5));
            await Assert.That(stack.Node.IsReady).IsTrue();

            var sweepFailed = new TaskCompletionSource<Exception>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            stack.Node.Error += (_, e) =>
            {
                if (e.Exception is RestException { StatusCode: 500 })
                    sweepFailed.TrySetResult(e.Exception);
                return Task.CompletedTask;
            };
            mode = 2;
            await stack.Server.DropConnectionAsync(1000, "orphan 500 drop").ConfigureAwait(false);
            await sweepFailed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await Assert.That(stack.Node.IsReady).IsTrue();
            await Assert.That(destroyed.Count).IsEqualTo(3);
        }
        finally
        {
            await stack.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ConnectNodeThrow()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions { Name = "canceled-connect", ReadyTimeout = TimeSpan.FromSeconds(5) }
        );
        extension.AddNode(node);
        var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert
            .That(async () =>
                await extension.ConnectAllNodesAsync(canceled.Token).ConfigureAwait(false)
            )
            .Throws<OperationCanceledException>();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();

        var server2 = new FakeNodeServer();
        var bridge2 = new FakeBridge();
        bridge2.GoReady();
        var extension2 = new NyraExtension(bridge2);
        await extension2.DisposeAsync().ConfigureAwait(false);
        var late = new Node(
            new Endpoint("127.0.0.1", server2.Port, server2.Password),
            new NodeOptions { Name = "late-node", ReadyTimeout = TimeSpan.FromSeconds(5) }
        );
        await Assert
            .That(async () =>
            {
                extension2.AddNode(late);
                await extension2.ConnectAllNodesAsync().ConfigureAwait(false);
            })
            .Throws<ObjectDisposedException>();
        server2.Dispose();
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public int Warnings;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                Warnings++;
        }
    }

    [Test]
    public async Task ReadyWatchdogAbort()
    {
        var server = new FakeNodeServer { SuppressReady = true };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "watchdog",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromMilliseconds(500),
            }
        );
        extension.AddNode(node);
        var connectAll = extension.ConnectAllNodesAsync();
        await Assert
            .That(() => Volatile.Read(ref server.WebsocketConnections))
            .WaitsFor(assert => assert.IsGreaterThanOrEqualTo(2), TimeSpan.FromSeconds(5));
        await Assert.That(node.IsReady).IsFalse();
        await extension.DisposeAsync().ConfigureAwait(false);
        try
        {
            await connectAll.ConfigureAwait(false);
        }
        catch (Exception) { }
        server.Dispose();
    }
}
