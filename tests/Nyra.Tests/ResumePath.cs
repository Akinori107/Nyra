using Nyra.Abstractions;
using Nyra.Exceptions;

namespace Nyra.Tests;

public sealed class ResumePath
{
    private static async Task<Node> ConnectNodeAsync(
        FakeNodeServer server,
        NyraExtension extension,
        string name
    )
    {
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = name,
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return node;
    }

    [Test]
    public async Task HostileSessionDisablesResuming()
    {
        var server = new FakeNodeServer { SessionId = "bad id" };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = await ConnectNodeAsync(server, extension, "hostile-resume")
            .ConfigureAwait(false);

        await Assert.That(node.IsReady).IsTrue();
        await Assert
            .That(server.RestCalls.Any(c => c is { Method: "PATCH", Path: "/v4/sessions/bad id" }))
            .IsFalse();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task ResumePatchFailure()
    {
        var server = new FakeNodeServer();
        server.On("PATCH", "/v4/sessions/fake-session", 500, """{"message":"no"}""");
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "resume-fail",
                Resuming = true,
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        var errors = new RecordingList<Exception>();
        node.Error += (_, args) =>
        {
            errors.Add(args.Exception);
            return Task.CompletedTask;
        };

        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        await Assert
            .That(() => errors.Snapshot().Any(IsResumeFailure))
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        await Assert
            .That(
                server.RestCalls.Any(c =>
                    c is { Method: "PATCH", Path: "/v4/sessions/fake-session" }
                )
            )
            .IsTrue();
        await Assert.That(node.IsReady).IsTrue();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    private static bool IsResumeFailure(Exception exception) =>
        exception.InnerException is RestException;

    private sealed class BreakingBridge : IDiscordBridge, IDisposable
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
        ) => Task.FromResult(new ClientInformation(FakeBridge.UserId));

        public void Dispose() => throw new InvalidOperationException("the bridge broke on dispose");
    }

    [Test]
    public async Task BreakingBridgeDispose()
    {
        var extension = new NyraExtension(new BreakingBridge());
        await Assert
            .That(async () => await extension.DisposeAsync().ConfigureAwait(false))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("the bridge broke on dispose");
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

        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new InvalidOperationException("the async bridge broke"));
    }

    private sealed class SlowAsyncBridge : IDiscordBridge, IAsyncDisposable
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
        ) => Task.FromResult(new ClientInformation(FakeBridge.UserId));

        public ValueTask DisposeAsync() => new(Task.Delay(TimeSpan.FromSeconds(8)));
    }

    [Test]
    public async Task AsyncBridgeDisposeShapes()
    {
        var faulting = new NyraExtension(new FaultingAsyncBridge());
        await Assert
            .That(() =>
            {
                faulting.Dispose();
            })
            .Throws<InvalidOperationException>()
            .WithMessageContaining("the async bridge broke");

        var slow = new NyraExtension(new SlowAsyncBridge());
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Assert.That(() => slow.Dispose()).ThrowsNothing();
        await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(6));
    }

    [Test]
    public async Task StuckHandlerDrain()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "stuck-drain",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        node.Disconnected += async (_, _) =>
            await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        await Assert
            .That(async () => await extension.DisposeAsync().ConfigureAwait(false))
            .ThrowsNothing();
        await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(10));
        server.Dispose();
    }
}
