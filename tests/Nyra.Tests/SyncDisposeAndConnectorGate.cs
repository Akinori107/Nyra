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
    public async Task SyncDisposeExplodingBridge()
    {
        var bridge = new ExplodingDisposeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert
            .That(extension.Dispose)
            .Throws<InvalidOperationException>()
            .WithMessageContaining("exploded");
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
}
