using Microsoft.Extensions.Logging;

namespace Nyra.Tests;

public sealed class NodeAndExtensionSurface
{
    [Test]
    public async Task LogAttempt()
    {
        var server = new FakeNodeServer();
        var logger = new AttemptLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "surface-attempts",
                ReconnectDelay = TimeSpan.FromMilliseconds(40),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(80),
                MaxReconnectAttempts = 5,
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

        server.Dispose();
        var deadline = Environment.TickCount64 + 20_000;
        while (Environment.TickCount64 < deadline && !final.Task.IsCompleted)
            await Task.Delay(50).ConfigureAwait(false);
        await Assert.That(final.Task.IsCompleted).IsTrue();

        for (var attempt = 1; attempt <= 5; attempt++)
            await extension.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class AttemptLogger : ILogger
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Lines.Add(formatter(state, exception));
    }

    [Test]
    public async Task BestNode()
    {
        var bare = new NyraExtension(new FakeBridge());
        await Assert.That(bare.GetNode()).IsNull();

        using var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "surface-best",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();
        await Assert.That(ReferenceEquals(extension.GetNode(), node)).IsTrue();
        await node.DisconnectAsync().ConfigureAwait(false);
    }
}
