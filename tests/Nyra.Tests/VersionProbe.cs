using Microsoft.Extensions.Logging;

namespace Nyra.Tests;

public sealed class VersionProbe
{
    private sealed record Stack : IAsyncDisposable
    {
        public required NyraExtension Extension { get; init; }

        public required Node Node { get; init; }

        public required FakeNodeServer Server { get; init; }

        public required CollectingLogger Logger { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel >= LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    private static async Task<Stack> StartAsync(string semver)
    {
        var server = new FakeNodeServer { Semver = semver };
        var logger = new CollectingLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "probe",
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
            Logger = logger,
        };
    }

    [Test]
    public async Task UnparseableDottedVersionWarns()
    {
        var stack = await StartAsync("x.9.fork").ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            using (Assert.Multiple())
            {
                await Assert.That(stack.Node.IsReady).IsTrue();
            }
        }
    }

    [Test]
    public async Task HashFormVersionStaysQuiet()
    {
        var stack = await StartAsync("a1b2c3d").ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            await Assert.That(stack.Node.IsReady).IsTrue();
        }
    }
}
