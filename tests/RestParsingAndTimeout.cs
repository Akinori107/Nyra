using Microsoft.Extensions.Logging;

namespace Nyra.Tests;

public sealed class RestParsingAndTimeout
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

    private sealed class CaptureLogger : ILogger
    {
        public sealed record Entry(LogLevel Level, string Message, string? Exception);

        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel level,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (Entries)
                Entries.Add(new Entry(level, formatter(state, exception), exception?.Message));
        }
    }

    private static async Task<Stack> StartAsync(
        FakeNodeServer? server = null,
        Action<NodeOptions>? tune = null
    )
    {
        server ??= new FakeNodeServer();
        var logger = new CaptureLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var options = new NodeOptions
        {
            Name = "rest-parsing",
            Resuming = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(50),
            MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
            VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
            ReadyTimeout = TimeSpan.FromSeconds(5),
        };
        tune?.Invoke(options);
        var node = new Node(new Endpoint("127.0.0.1", server.Port, server.Password), options);
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
    public async Task ProbeTimeout()
    {
        var server = new FakeNodeServer { GhostProbes = true };
        var logger = new CaptureLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "probe-ghost",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromMilliseconds(700),
                MaxReconnectAttempts = 0,
            }
        );
        extension.AddNode(node);

        var final = new TaskCompletionSource<Exception?>();
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                final.TrySetResult(args.Exception);
            return Task.CompletedTask;
        };

        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert
            .That(() => final.Task.IsCompleted)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await Assert.That(node.IsReady).IsFalse();
        await Assert.That(node.IsConnected).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task HandshakeTimeout()
    {
        var server = new FakeNodeServer { GhostHandshakes = true };
        var logger = new CaptureLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "handshake-ghost",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromMilliseconds(700),
                MaxReconnectAttempts = 0,
            }
        );
        extension.AddNode(node);

        var final = new TaskCompletionSource<Exception?>();
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                final.TrySetResult(args.Exception);
            return Task.CompletedTask;
        };

        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert
            .That(() => final.Task.IsCompleted)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await Assert.That(node.IsReady).IsFalse();
        await Assert.That(node.IsConnected).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task GiveUpInitialWithLabels()
    {
        var dead = new FakeNodeServer();
        var port = dead.Port;
        dead.Dispose();
        var logger = new CaptureLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", port, "pass"),
            new NodeOptions
            {
                Name = "labeled",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(100),
                MaxReconnectAttempts = 2,
            }
        );
        extension.AddNode(node);

        var final = new TaskCompletionSource<Exception?>();
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                final.TrySetResult(args.Exception);
            return Task.CompletedTask;
        };

        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert
            .That(() => final.Task.IsCompleted)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(20));
        using (Assert.Multiple()) { }

        await extension.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task GiveUpOnKilledConnection()
    {
        var server = new FakeNodeServer();
        var logger = new CaptureLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "killed",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                MaxReconnectAttempts = 0,
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();

        var final = new TaskCompletionSource<Exception?>();
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                final.TrySetResult(args.Exception);
            return Task.CompletedTask;
        };

        server.KillConnection();
        await Assert
            .That(() => final.Task.IsCompleted)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await Assert.That(await final.Task.ConfigureAwait(false)).IsNotNull();
        await Assert.That(node.IsReady).IsFalse();
        await Assert.That(node.IsConnected).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task DropWithLogger()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);
            await Assert.That(node.IsReady).IsTrue();

            var reconnected = new TaskCompletionSource<bool>();
            node.Connected += (_, _) =>
            {
                reconnected.TrySetResult(true);
                return Task.CompletedTask;
            };

            await server.DropConnectionAsync(1000, "logged drop").ConfigureAwait(false);
            await Assert
                .That(
                    await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false)
                )
                .IsTrue();
        }
    }
}
