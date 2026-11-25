using Microsoft.Extensions.Logging;
using Nyra.Abstractions;
using Nyra.EventArgs;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class ReadyAndRegistryRace
{
    internal sealed class HostileLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public RecordingList<string> Messages { get; } = [];

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Messages.Add(formatter(state, exception));
            throw new InvalidOperationException("the logger bit back");
        }
    }

    private static async Task<NodeStack> StartAsync(
        string name = "ready-registry",
        TimeProvider? time = null
    )
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge, time);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = name,
                TimeProvider = time ?? TimeProvider.System,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(250),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new NodeStack
        {
            Extension = extension,
            Node = node,
            Bridge = bridge,
            Server = server,
        };
    }

    [Test]
    public async Task NonResumedWarningHostileLogger()
    {
        var stack = await StartAsync("non-resumed-hostile").ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            server.ResumeOnReconnect = false;
            bridge.Logger = new HostileLogger();

            var restored = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            conn.GuildConnectionRestored += (_, _) =>
            {
                restored.TrySetResult();
                return Task.CompletedTask;
            };
            var readyAgain = new TaskCompletionSource<NodeReadyEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            node.Ready += (_, args) =>
            {
                readyAgain.TrySetResult(args);
                return Task.CompletedTask;
            };

            await server.DropConnectionAsync(1000, "hostile warning").ConfigureAwait(false);
            var ready = await readyAgain
                .Task.WaitAsync(TimeSpan.FromSeconds(8))
                .ConfigureAwait(false);

            await Assert.That(ready.Resumed).IsFalse();
            await Assert
                .That(() => restored.Task.IsCompletedSuccessfully)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Assert.That(node.IsReady).IsTrue();
            bridge.Logger = null;
        }
    }

    [Test]
    public async Task BridgeFaultFastDispose()
    {
        var bridge = new FaultingAsyncBridge();
        var extension = new NyraExtension(bridge);
        await Assert
            .That(extension.Dispose)
            .Throws<InvalidOperationException>()
            .WithMessage("bridge dispose refused");
    }

    private sealed class FaultingAsyncBridge : IDiscordBridge, IAsyncDisposable
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

        public ILogger? Logger => null;

        public Task<ClientInformation> WaitForReadyAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ClientInformation(7));

        public Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new InvalidOperationException("bridge dispose refused"));
    }
}
