using Nyra.Abstractions;

namespace Nyra.Tests;

public sealed class SyncDisposeAndConnectorGate
{
    private sealed class ExplodingDisposeBridge : IDiscordBridge, IAsyncDisposable
    {
        private readonly TaskCompletionSource<ClientInformation> _ready = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated
        {
            add { }
            remove { }
        }

        public event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated
        {
            add { }
            remove { }
        }

        public Microsoft.Extensions.Logging.ILogger? Logger { get; set; }

        public Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task<ClientInformation> WaitForReadyAsync(
            CancellationToken cancellationToken = default
        ) => _ready.Task.WaitAsync(cancellationToken);

        public void GoReady() => _ready.TrySetResult(new ClientInformation(FakeBridge.UserId));

        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new InvalidOperationException("bridge disposal exploded"));
    }

    [Test]
    public async Task SyncDisposeExplodingBridge()
    {
        var bridge = new ExplodingDisposeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert
            .That(() => extension.Dispose())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("exploded");
    }

    private sealed class HostileLogger : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public RecordingList<string> Messages { get; } = new();

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Messages.Add(formatter(state, exception));
            throw new InvalidOperationException("the logger bit back");
        }
    }

    private static async Task<Stack> StartAsync(FakeNodeServer server, FakeBridge bridge)
    {
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "sync-dispose-logs",
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
            Bridge = bridge,
            Server = server,
        };
    }

    [Test]
    public async Task HostileReadyFlowLogs()
    {
        var server = new FakeNodeServer { SuppressReady = true };
        var bridge = new FakeBridge { Logger = new HostileLogger() };
        server.On("PATCH", "/v4/sessions/fake-session", 200, "{}");
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "sync-dispose-logs",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);

        async Task WaitConnectionsAsync(int target)
        {
            for (var i = 0; i < 150; i++)
            {
                if (server.WebsocketConnections >= target)
                    return;
                await Task.Delay(50).ConfigureAwait(false);
            }
        }

        async Task AwaitReadyAsync(int connectionsTarget, string payload)
        {
            await WaitConnectionsAsync(connectionsTarget).ConfigureAwait(false);
            await Task.Delay(150).ConfigureAwait(false);
            await server.PushAsync(payload).ConfigureAwait(false);
            for (var i = 0; i < 100; i++)
            {
                if (node.IsReady)
                    return;
                await Task.Delay(50).ConfigureAwait(false);
            }
        }

        var connect = extension.ConnectAllNodesAsync();
        await AwaitReadyAsync(1, """{"op":"ready","resumed":false,"sessionId":"fake-session"}""")
            .ConfigureAwait(false);
        await connect.ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();

        await node.UpdateSessionAsync(true, TimeSpan.FromSeconds(60)).ConfigureAwait(false);

        server.KillConnection();
        await AwaitReadyAsync(2, """{"op":"ready","resumed":false}""").ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();
        await Assert.That(node.SessionId).IsNull();

        server.KillConnection();
        await AwaitReadyAsync(3, """{"op":"ready","resumed":false,"sessionId":"fake-session"}""")
            .ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();
        await Assert.That(node.SessionId).IsEqualTo("fake-session");
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task SuppressedErrorWarning()
    {
        var logger = new HostileLogger();
        var stack = await StartAsync(new FakeNodeServer(), new FakeBridge { Logger = logger })
            .ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var raised = new RecordingList<string>();
            stack.Node.Error += (_, e) =>
            {
                raised.Add(e.Exception.Message);
                return Task.CompletedTask;
            };

            for (var strike = 1; strike <= 8; strike++)
                stack.Node.HandleError(
                    new InvalidOperationException($"sync dispose: strike {strike}")
                );

            await Assert
                .That(() => raised.Count == 5)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Task.Delay(100).ConfigureAwait(false);
            await Assert.That(raised.Count).IsEqualTo(5);
        }
    }
}
