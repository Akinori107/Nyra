using Nyra.Abstractions;

namespace Nyra.Tests;

public sealed class GuildRemoval
{
    private sealed class ThrowingWireBridge : IDiscordBridge
    {
        public event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated
        {
            add { }
            remove { }
        }

        public event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated
        {
            add => throw new InvalidOperationException("the wiring broke");
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
        ) => Task.FromResult(new ClientInformation(FakeBridge.UserId));
    }

    [Test]
    public async Task WiringRollback()
    {
        var server = new FakeNodeServer();
        var recorder = new RecordingLogger();
        var bridge = new ThrowingWireBridge { Logger = recorder };
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "unwired",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                MaxReconnectAttempts = 2,
                ReadyTimeout = TimeSpan.FromMilliseconds(700),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(2),
            }
        );
        extension.AddNode(node);

        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        for (var i = 0; i < 80 && !node.IsReady; i++)
            await Task.Delay(25).ConfigureAwait(false);
        await Assert.That(node.IsReady).IsFalse();
        await Assert.That(node.IsConnected).IsFalse();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Messages { get; } = [];

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception));

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }

    private sealed class BlankIdBridge : IDiscordBridge
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
        ) => Task.FromResult(default(ClientInformation));
    }

    [Test]
    public async Task BlankIdentification()
    {
        var server = new FakeNodeServer();
        var extension = new NyraExtension(new BlankIdBridge());
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "blank-id",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);

        await extension
            .ConnectAllNodesAsync()
            .WaitAsync(TimeSpan.FromSeconds(8))
            .ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);
        await Assert.That(node.IsReady).IsFalse();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }
}
