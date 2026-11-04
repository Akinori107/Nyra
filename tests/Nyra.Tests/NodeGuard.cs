using Microsoft.Extensions.Logging;
using Nyra.Exceptions;

namespace Nyra.Tests;

public sealed class NodeGuard
{
    [Test]
    public async Task TheOldFloorRejects()
    {
        using var server = new FakeNodeServer { Semver = "3.8.0" };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "guards",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);

        IncompatibleServerException? tooOld = null;
        try
        {
            await node.EnsureServerInfoAsync(default, forceProbe: true).ConfigureAwait(false);
        }
        catch (IncompatibleServerException exception)
        {
            tooOld = exception;
        }
        using (Assert.Multiple())
        {
            await Assert.That(tooOld).IsNotNull();
        }
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task TheSecondBridgeRefuses()
    {
        using var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "attached",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        var other = new FakeBridge();
        other.GoReady();
        await Assert
            .That(async () => await node.ConnectAsync(other, null, default).ConfigureAwait(false))
            .Throws<InvalidOperationException>();
        await Assert.That(node.IsReady).IsTrue();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task TheThrowingLoggerSkips()
    {
        using var server = new FakeNodeServer { Semver = "x.9.fork" };
        var logger = new ThrowingLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "throwing",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();
        await Assert.That(logger.Calls).IsGreaterThan(0);
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    private sealed class ThrowingLogger : ILogger
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Interlocked.Increment(ref _calls);
            throw new InvalidOperationException("the logger refuses to log");
        }
    }
}
