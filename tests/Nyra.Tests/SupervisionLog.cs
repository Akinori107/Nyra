using Microsoft.Extensions.Logging;

namespace Nyra.Tests;

public sealed class SupervisionLog
{
    private static async Task<Node> ConnectNodeAsync(
        FakeNodeServer server,
        NyraExtension extension,
        string name,
        int maxReconnectAttempts = -1,
        TimeSpan? reconnectDelay = null
    )
    {
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = name,
                Resuming = true,
                ReconnectDelay = reconnectDelay ?? TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = (reconnectDelay ?? TimeSpan.FromMilliseconds(50)) * 4,
                ReadyTimeout = TimeSpan.FromSeconds(5),
                MaxReconnectAttempts = maxReconnectAttempts,
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return node;
    }

    [Test]
    public async Task ThrowingLogsDoNotEndTheSupervision()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = await ConnectNodeAsync(server, extension, "log-throw").ConfigureAwait(false);

        var logger = new ThrowingLogger();
        bridge.Logger = logger;
        await server.DropConnectionAsync(1000, "hostile logger").ConfigureAwait(false);

        await Assert
            .That(() => Volatile.Read(ref server.WebsocketConnections) >= 2)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await Assert
            .That(() => node.IsReady && node.IsConnected)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await Assert.That(logger.Calls).IsGreaterThan(0);

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task ThrowingLogDoesNotBreakTheGiveUp()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = await ConnectNodeAsync(
                server,
                extension,
                "log-throw-giveup",
                maxReconnectAttempts: 1
            )
            .ConfigureAwait(false);

        var final = new TaskCompletionSource<Exception?>();
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                final.TrySetResult(args.Exception);
            return Task.CompletedTask;
        };

        bridge.Logger = new ThrowingLogger();
        server.InfoStatus = 401;
        await server.DropConnectionAsync(1000, "turning hostile").ConfigureAwait(false);

        await Assert
            .That(() => final.Task.IsCompleted)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await Assert.That(node.IsConnected).IsFalse();
        await Assert.That(node.IsReady).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task StopDuringTheRetryDelay()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = await ConnectNodeAsync(
                server,
                extension,
                "log-stop-delay",
                reconnectDelay: TimeSpan.FromMilliseconds(500)
            )
            .ConfigureAwait(false);

        var final = new TaskCompletionSource<Exception?>();
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                final.TrySetResult(args.Exception);
            return Task.CompletedTask;
        };

        await server.DropConnectionAsync(1000, "stop window").ConfigureAwait(false);
        await Task.Delay(150).ConfigureAwait(false);
        await node.DisconnectAsync().ConfigureAwait(false);

        await Assert.That(node.IsConnected).IsFalse();
        await Task.Delay(1500).ConfigureAwait(false);
        await Assert.That(final.Task.IsCompleted).IsFalse();
        await Assert.That(node.IsReady).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    private sealed class ThrowingLogger : ILogger
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("the logger itself is broken");
        }

        public bool IsEnabled(LogLevel logLevel) => false;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }
}
