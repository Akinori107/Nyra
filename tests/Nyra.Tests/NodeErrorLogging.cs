using Microsoft.Extensions.Logging;

namespace Nyra.Tests;

public sealed class NodeErrorLogging
{
    private const string StatsJson =
        """{"op":"stats","players":1,"playingPlayers":1,"uptime":500,"memory":{"free":1,"used":2,"allocated":3,"reservable":4},"cpu":{"cores":2,"systemLoad":0.1,"lavalinkLoad":0.2}}""";

    private static NodeOptions Options(string name) =>
        new()
        {
            Name = name,
            Resuming = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(50),
            MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
            VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
            ReadyTimeout = TimeSpan.FromSeconds(5),
        };

    [Test]
    public async Task ThrowingHandlerLandsOnTheNodeLog()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var logger = new RecordingLogger();
        bridge.Logger = logger;
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            Options("floors-log")
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        var survived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        node.StatsUpdated += (_, _) => throw new InvalidOperationException("the handler exploded");
        node.StatsUpdated += (_, _) =>
        {
            survived.TrySetResult();
            return Task.CompletedTask;
        };
        await server.PushAsync(StatsJson).ConfigureAwait(false);
        await Assert
            .That(
                await Task.WhenAny(survived.Task, Task.Delay(TimeSpan.FromSeconds(5)))
                    .ConfigureAwait(false)
            )
            .IsSameReferenceAs(survived.Task);

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task ThrowingLoggerCannotKillTheRaise()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            Options("floors-throw")
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        var logger = new ThrowingLogger();
        bridge.Logger = logger;
        var landings = 0;
        node.StatsUpdated += (_, _) => throw new InvalidOperationException("no reporter left");
        node.StatsUpdated += (_, _) =>
        {
            Interlocked.Increment(ref landings);
            return Task.CompletedTask;
        };
        await server.PushAsync(StatsJson).ConfigureAwait(false);
        await server.PushAsync(StatsJson).ConfigureAwait(false);
        await Assert
            .That(() => Volatile.Read(ref landings) >= 2)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        await Assert.That(Volatile.Read(ref landings)).IsEqualTo(2);
        await Assert.That(logger.Calls).IsGreaterThan(0);

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task DisposedNodeLogsBeforeFailingFast()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var logger = new RecordingLogger();
        bridge.Logger = logger;
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            Options("floors-dead")
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        node.Dispose();
        await Assert
            .That(async () =>
            {
                await node.ConnectAsync(bridge, extension, CancellationToken.None)
                    .ConfigureAwait(false);
            })
            .Throws<TaskCanceledException>();

        await Assert
            .That(async () =>
            {
                await node.LoadTracksAsync("atwa").ConfigureAwait(false);
            })
            .Throws<ObjectDisposedException>();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task StoppedMidConnectNamesTheStop()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        var logger = new RecordingLogger();
        bridge.Logger = logger;
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            Options("floors-midstop")
        );
        extension.AddNode(node);

        var connecting = node.ConnectAsync(bridge, extension, CancellationToken.None);
        await Task.Delay(150).ConfigureAwait(false);
        await node.DisconnectAsync().ConfigureAwait(false);
        bridge.GoReady();

        await Assert
            .That(async () => await connecting.ConfigureAwait(false))
            .Throws<InvalidOperationException>();
        await Assert.That(node.IsConnected).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task EmptyExtensionAnswersNoConnection()
    {
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        await Assert.That(extension.GetConnection(777)).IsNull();
        await extension.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task BreakingBridgeDisposeUnderThrowingLogger()
    {
        var logger = new ThrowingLogger();
        var bridge = new BreakingBridge { Logger = logger };
        var extension = new NyraExtension(bridge);
        extension.AddNode(new Node(new Endpoint("127.0.0.1", 1, "pass"), Options("floors-bridge")));

        await Assert
            .That(async () => await extension.DisposeAsync().ConfigureAwait(false))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("broke on dispose");
        await Assert.That(extension.IsDisposed).IsTrue();
    }

    [Test]
    public async Task ThrowingForwardLoggerCannotKillTheForward()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            Options("floors-forward")
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        var logger = new ThrowingLogger();
        bridge.Logger = logger;
        var landings = 0;
        extension.NodeStatsUpdated += (_, _) =>
            throw new InvalidOperationException("the forward blew");
        extension.NodeStatsUpdated += (_, _) =>
        {
            Interlocked.Increment(ref landings);
            return Task.CompletedTask;
        };
        await server.PushAsync(StatsJson).ConfigureAwait(false);
        await server.PushAsync(StatsJson).ConfigureAwait(false);
        await Assert
            .That(() => Volatile.Read(ref landings) >= 2)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        await Assert.That(Volatile.Read(ref landings)).IsEqualTo(2);
        await Assert.That(logger.Calls).IsGreaterThan(0);

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }

    private sealed class ThrowingLogger : ILogger
    {
        public int Calls;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("the logger itself is broken");
        }

        public bool IsEnabled(LogLevel logLevel) => false;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }

    private sealed class BreakingBridge : Nyra.Abstractions.IDiscordBridge, IDisposable
    {
        public event EventHandler<Nyra.Abstractions.VoiceServerUpdatedEventArgs>? VoiceServerUpdated
        {
            add { }
            remove { }
        }

        public event EventHandler<Nyra.Abstractions.VoiceStateUpdatedEventArgs>? VoiceStateUpdated
        {
            add { }
            remove { }
        }

        public ILogger? Logger { get; set; }

        public Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task<Nyra.Abstractions.ClientInformation> WaitForReadyAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new Nyra.Abstractions.ClientInformation(FakeBridge.UserId));

        public void Dispose() => throw new InvalidOperationException("the bridge broke on dispose");
    }
}
