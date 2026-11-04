using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Nyra.Tests;

public sealed class GatewayWait
{
    private sealed class CollectingLogger : ILogger
    {
        public List<string> Errors { get; } = [];

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
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }

    [Test]
    public async Task AGatewayThatNeverReadiesTimesOut()
    {
        var time = new FakeTimeProvider();
        var server = new FakeNodeServer();
        var logger = new CollectingLogger();
        var bridge = new FakeBridge { Logger = logger };
        var extension = new NyraExtension(bridge, time);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "gateway-wait",
                TimeProvider = time,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(2),
            }
        );
        extension.AddNode(node);

        try
        {
            var connect = extension.ConnectAllNodesAsync();
            await NodeHarness
                .AdvanceUntilAsync(time, connect, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(75))
                .ConfigureAwait(false);
            await Assert
                .That(async () => await connect.ConfigureAwait(false))
                .Throws<TimeoutException>();
        }
        finally
        {
            await extension.DisposeAsync().ConfigureAwait(false);
            server.Dispose();
        }
    }
}
