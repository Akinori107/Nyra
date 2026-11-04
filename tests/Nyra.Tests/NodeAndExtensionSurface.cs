using Microsoft.Extensions.Logging;

namespace Nyra.Tests;

public sealed class NodeAndExtensionSurface
{
    [Test]
    public async Task NodeGetter()
    {
        var stranger = new Node(
            new Nyra.Endpoint("127.0.0.1", 1, "pass"),
            new Nyra.NodeOptions { Name = "surface-stranger" }
        );
        await Assert.That(stranger.IsNodeLink).IsFalse();
        await Assert.That(stranger.Version).IsNull();

        using var lavalink = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var lavaNode = new Node(
            new Nyra.Endpoint("127.0.0.1", lavalink.Port, lavalink.Password),
            new Nyra.NodeOptions { Name = "surface-lava" }
        );
        extension.AddNode(lavaNode);
        await lavaNode.EnsureServerInfoAsync(default, forceProbe: true).ConfigureAwait(false);
        await Assert.That(lavaNode.IsNodeLink).IsFalse();
        await Assert.That(lavaNode.Version is { Major: 4 }).IsTrue();

        using var nodelink = new FakeNodeServer();
        var linkNode = new Node(
            new Nyra.Endpoint("127.0.0.1", nodelink.Port, nodelink.Password),
            new Nyra.NodeOptions { Name = "surface-link" }
        );
        extension.AddNode(linkNode);
        await linkNode.EnsureServerInfoAsync(default, forceProbe: true).ConfigureAwait(false);
        await Assert.That(linkNode.IsNodeLink).IsTrue();
        await Assert.That(linkNode.Version).IsNotNull();
    }

    [Test]
    public async Task LogSafe()
    {
        using (Assert.Multiple())
        {
            await Assert.That(Node.LogSafe(null)).IsEqualTo(string.Empty);
            await Assert.That(Node.LogSafe("plain")).IsEqualTo("plain");
            await Assert.That(Node.LogSafe("bad\u0001")).IsEqualTo("bad");
            await Assert.That(Node.LogSafe("ação")).IsEqualTo("ao");
        }
    }

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
            new Nyra.Endpoint("127.0.0.1", server.Port, server.Password),
            new Nyra.NodeOptions
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
