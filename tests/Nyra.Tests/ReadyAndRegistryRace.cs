using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;
using Nyra.EventArgs;
using Nyra.Exceptions;
using Nyra.Internal;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class ReadyAndRegistryRace
{
    internal sealed class HostileLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public RecordingList<string> Messages { get; } = new();

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

    private static async Task<SupervisionLoopRace.Stack> StartAsync(string name = "ready-registry")
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = name,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(250),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new SupervisionLoopRace.Stack
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

            var invalidated = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            conn.GuildConnectionInvalidated += (_, _) =>
            {
                invalidated.TrySetResult();
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
                .That(() => invalidated.Task.IsCompletedSuccessfully)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Assert.That(node.IsReady).IsTrue();
            bridge.Logger = null;
        }
    }

    [Test]
    public async Task ExtensionDisposeOutwaitsNode()
    {
        var stack = await StartAsync("ext-timeout").ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;
            var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var holder = Task.Run(() =>
            {
                lock (node.EventsLock)
                {
                    held.TrySetResult();
                    hold.Task.Wait(TimeSpan.FromSeconds(40));
                }
            });
            await held.Task.ConfigureAwait(false);

            try
            {
                await Assert
                    .That(async () =>
                        await Task.Run(() => stack.Extension.Dispose()).ConfigureAwait(false)
                    )
                    .Throws<TimeoutException>();
            }
            finally
            {
                hold.TrySetResult();
                await holder.ConfigureAwait(false);
            }
        }
    }

    [Test]
    public async Task BridgeFaultFastDispose()
    {
        var bridge = new FaultingAsyncBridge();
        var extension = new NyraExtension(bridge);
        await Assert
            .That(() => extension.Dispose())
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

        public ILogger? Logger { get; set; }

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

        public ClientInformation Identification => new(7);

        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new InvalidOperationException("bridge dispose refused"));
    }

    [Test]
    public async Task RestPrematureBody()
    {
        var stack = await StartAsync("rest-premature").ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            stack.Server.OnRaw(
                "GET",
                "/v4/sessions/fake-session/groups",
                async (_, stream) =>
                {
                    var head =
                        "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                        + "Content-Length: 5000\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(head)).ConfigureAwait(false);
                    await stream
                        .WriteAsync(Encoding.UTF8.GetBytes("{\"half\":\"of the body"))
                        .ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                    await Task.Delay(700).ConfigureAwait(false);
                    stream.Close();
                }
            );

            await Assert
                .That(async () =>
                {
                    await stack
                        .Node.Rest.Groups.ListAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                })
                .Throws<NodeUnreachableException>();
        }
    }

    [Test]
    public async Task RetryAfterPastDate()
    {
        var response = new HttpResponseMessage();
        response.Headers.RetryAfter = new RetryConditionHeaderValue(
            DateTimeOffset.UtcNow - TimeSpan.FromHours(1)
        );
        await Assert.That(RestClient.ReadRetryAfter(response)).IsNull();
    }

    [Test]
    public async Task LoadStreamCallerCancel()
    {
        var stack = await StartAsync("stream-cancel").ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            stack.Server.OnRaw(
                "GET",
                "/v4/loadstream",
                async (_, stream) =>
                {
                    var head =
                        "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\n"
                        + "Content-Length: 500\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(head)).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            );

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert
                .That(async () =>
                {
                    _ = await stack
                        .Node.Rest.Tracks.OpenLoadStreamAsync(
                            "track",
                            100,
                            0,
                            null,
                            LoadStreamFormat.Raw,
                            cancelled.Token
                        )
                        .ConfigureAwait(false);
                })
                .Throws<OperationCanceledException>();
        }
    }
}
