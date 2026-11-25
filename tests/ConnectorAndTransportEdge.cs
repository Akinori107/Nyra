using Microsoft.Extensions.Time.Testing;
using Nyra.Abstractions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class ConnectorAndTransportEdge
{
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
                Name = "transport-edge",
                TimeProvider = time ?? TimeProvider.System,
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

    private sealed class HostileLogger : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public RecordingList<string> Messages { get; } = [];

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

    private sealed class FatalGatewayBridge : IDiscordBridge
    {
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
        ) =>
            Task.FromException<ClientInformation>(
                new InvalidOperationException("the gateway died on boot")
            );
    }

    private sealed class NeverReadyBridge : IDiscordBridge
    {
        private readonly TaskCompletionSource<ClientInformation> _never = new(
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
        ) => _never.Task.WaitAsync(cancellationToken);
    }

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

        public Microsoft.Extensions.Logging.ILogger? Logger => null;

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
    public async Task GatewayFatalWithHostileLogger()
    {
        var bridge = new FatalGatewayBridge { Logger = new HostileLogger() };
        var extension = new NyraExtension(bridge);
        var fatal = await Assert
            .That(async () => await extension.ConnectAllNodesAsync().ConfigureAwait(false))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("the gateway died on boot");
        await Assert.That(fatal!.Message).Contains("gateway died");
        await extension.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task BridgeDisposeThrows()
    {
        var bridge = new ExplodingDisposeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert
            .That(async () => await bridge.DisposeAsync().ConfigureAwait(false))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("exploded");
        await Assert
            .That(async () => await extension.DisposeAsync().ConfigureAwait(false))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("exploded");
    }

    [Test]
    public async Task WsCleanDrop()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            await stack.Server.DropConnectionAsync(1000, "clean bye").ConfigureAwait(false);
            await Assert
                .That(() => !stack.Node.IsConnected)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task WsCloseRefused()
    {
        var server = new FakeNodeServer { IgnoreWebsocketClose = true };
        var stack = await StartAsync(server).ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var disposal = Task.Run(() => stack.Extension.DisposeAsync().AsTask());
            var finished = await Task.WhenAny(disposal, Task.Delay(TimeSpan.FromSeconds(15)))
                .ConfigureAwait(false);
            await Assert.That(finished).IsSameReferenceAs(disposal);
        }
    }

    [Test]
    public async Task ConnectorDoubleConnect()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            await stack.Extension.ConnectAllNodesAsync().ConfigureAwait(false);
            await Assert.That(stack.Node.IsReady).IsTrue();
            await Assert.That(stack.Server.WebsocketConnections).IsEqualTo(1);
        }
    }

    [Test]
    public async Task ConnectorCancelledReadyWait()
    {
        var server = new FakeNodeServer { SuppressReady = true };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "transport-edge-silent",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert
            .That(async () =>
            {
                await extension.ConnectAllNodesAsync(cts.Token).ConfigureAwait(false);
            })
            .Throws<OperationCanceledException>();
        try
        {
            await extension.DisposeAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        server.Dispose();
    }

    [Test]
    public async Task GatewayWallHostileLogger()
    {
        var time = new FakeTimeProvider();
        var bridge = new NeverReadyBridge { Logger = new HostileLogger() };
        var extension = new NyraExtension(bridge, time);
        var connect = extension.ConnectAllNodesAsync();
        await AdvanceUntilAsync(time, connect, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(75))
            .ConfigureAwait(false);
        await Assert
            .That(async () => await connect.ConfigureAwait(false))
            .Throws<TimeoutException>();
        await extension.DisposeAsync().ConfigureAwait(false);
    }
}
