using Microsoft.Extensions.Logging;
using Nyra.Internal;

namespace Nyra.Tests;

public sealed class SupervisionLoopRace
{
    internal sealed record Stack : IAsyncDisposable
    {
        public required NyraExtension Extension { get; init; }

        public required Node Node { get; init; }

        public required FakeBridge Bridge { get; init; }

        public required FakeNodeServer Server { get; init; }

        public Task? ConnectTask { get; init; }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Extension.DisposeAsync().ConfigureAwait(false);
            }
            catch { }
            Server.Dispose();
        }
    }

    internal static async Task<Stack> StartAsync(
        FakeNodeServer? server = null,
        string name = "supervision",
        bool suppressReady = false,
        TimeSpan? readyTimeout = null,
        int maxReconnectAttempts = -1,
        TimeSpan? reconnectDelay = null
    )
    {
        server ??= new FakeNodeServer { SuppressReady = suppressReady };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var delay = reconnectDelay ?? TimeSpan.FromMilliseconds(50);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = name,
                ReconnectDelay = delay,
                MaxReconnectDelay = TimeSpan.FromMilliseconds(
                    Math.Max(250, delay.TotalMilliseconds + 100)
                ),
                ReadyTimeout = readyTimeout ?? TimeSpan.FromSeconds(5),
                MaxReconnectAttempts = maxReconnectAttempts,
            }
        );
        extension.AddNode(node);
        Task connectTask;
        if (suppressReady)
        {
            connectTask = extension.ConnectAllNodesAsync();
            await Task.Delay(400).ConfigureAwait(false);
        }
        else
        {
            await extension.ConnectAllNodesAsync().ConfigureAwait(false);
            connectTask = Task.CompletedTask;
        }
        return new Stack
        {
            Extension = extension,
            Node = node,
            Bridge = bridge,
            Server = server,
            ConnectTask = connectTask,
        };
    }

    private sealed class GenerationFlippingLogger : ILogger
    {
        private readonly NodeConnection _connection;
        private readonly int _target;

        public GenerationFlippingLogger(NodeConnection connection, int target)
        {
            _connection = connection;
            _target = target;
        }

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
            var rendered = formatter(state, exception);
            if (rendered.Contains("(attempt 1/1)", StringComparison.Ordinal))
                Reflect.Field(_connection, "_generation", _target);
        }
    }

    [Test]
    public async Task ReconnectDiscardedMidHandshake()
    {
        var server = new FakeNodeServer { HandshakeDelay = TimeSpan.FromSeconds(2) };
        var stack = await StartAsync(server, name: "discard-mid").ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            await stack.Server.DropConnectionAsync(1000, "discard test").ConfigureAwait(false);
            await Task.Delay(200).ConfigureAwait(false);

            await stack.Node.DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.Delay(2600).ConfigureAwait(false);

            await Assert.That(stack.Node.IsConnected).IsFalse();
        }
    }

    [Test]
    public async Task ConnectorReadyDuringGatewayWait()
    {
        var stack = await StartAsync(
                name: "late-gateway",
                reconnectDelay: TimeSpan.FromMilliseconds(300)
            )
            .ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var bridge = stack.Bridge;
            bridge.FailWaits();
            stack.Server.KillConnection();

            await Assert
                .That(() => !stack.Node.IsReady)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            var connectTask = stack.Node.ConnectAsync(
                bridge,
                stack.Extension,
                CancellationToken.None
            );
            await Assert
                .That(() => stack.Node.IsReady)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            bridge.GoReady();
            await connectTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            await Assert.That(connectTask.IsCompletedSuccessfully).IsTrue();
            await Assert.That(stack.Node.IsReady).IsTrue();
        }
    }

    [Test]
    public async Task ConnectorJoinsLiveSupervisionLoop()
    {
        var stack = await StartAsync(
                name: "join-live",
                reconnectDelay: TimeSpan.FromMilliseconds(400)
            )
            .ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            stack.Server.KillConnection();
            await Assert
                .That(() => !stack.Node.IsReady)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            var connectTask = stack.Node.ConnectAsync(
                stack.Bridge,
                stack.Extension,
                CancellationToken.None
            );
            await connectTask.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);

            await Assert.That(connectTask.IsCompletedSuccessfully).IsTrue();
            await Assert.That(stack.Node.IsReady).IsTrue();
        }
    }
}
