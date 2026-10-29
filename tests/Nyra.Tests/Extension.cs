using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;

namespace Nyra.Tests;

public sealed class Extension
{
    private sealed class DisposableBridge : IDiscordBridge, IDisposable
    {
        public int DisposeCount;

        public bool Disposed => DisposeCount > 0;

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

        public void Dispose() => Interlocked.Increment(ref DisposeCount);
    }

    private sealed class ThrowingSyncBridge : IDiscordBridge, IDisposable
    {
        public bool Threw;

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

        public Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task<ClientInformation> WaitForReadyAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ClientInformation(1));

        public void Dispose()
        {
            Threw = true;
            throw new InvalidOperationException("the bridge broke on dispose");
        }
    }

    private sealed class AsyncOnlyBridge : IDiscordBridge, IAsyncDisposable
    {
        public bool Disposed;

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

        public Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task<ClientInformation> WaitForReadyAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ClientInformation(1));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NeverReadyBridge : IDiscordBridge, IDisposable
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

        public ILogger? Logger { get; set; }

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

        public void Dispose() => _ready.TrySetCanceled();
    }

    private sealed class SilentServer : IDisposable
    {
        private readonly TcpListener _listener;

        public SilentServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        public int Port { get; }

        public void Dispose() => _listener.Dispose();
    }

    private static Node MakeNode(FakeNodeServer server, string name) =>
        new(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = name,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );

    [Test]
    public async Task AddNodeGuards()
    {
        var bridge = new FakeBridge();
        bridge.GoReady();
        var first = new NyraExtension(bridge);
        var second = new NyraExtension(bridge);
        var server = new FakeNodeServer();
        var node = MakeNode(server, "shared");
        first.AddNode(node);
        await Assert
            .That(() =>
            {
                second.AddNode(node);
            })
            .Throws<InvalidOperationException>();

        await Assert
            .That(() =>
            {
                first.AddNode(MakeNode(server, "SHARED"));
            })
            .Throws<InvalidOperationException>();

        await Assert
            .That(() =>
            {
                first.AddNode(MakeNode(server, "other-name"));
            })
            .Throws<InvalidOperationException>();

        var bracketed = new FakeNodeServer();
        var bracketExtension = new NyraExtension(bridge);
        bracketExtension.AddNode(
            new Node(new Endpoint("[::1]", bracketed.Port, "pass"), new NodeOptions { Name = "v6" })
        );
        await Assert
            .That(() =>
            {
                bracketExtension.AddNode(
                    new Node(
                        new Endpoint("[::1]", bracketed.Port, "pass"),
                        new NodeOptions { Name = "v6-again" }
                    )
                );
            })
            .Throws<InvalidOperationException>();

        first.Dispose();
        second.Dispose();
        bracketExtension.Dispose();
        server.Dispose();
        bracketed.Dispose();
    }

    [Test]
    public async Task StatsRouting()
    {
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var heavyServer = new FakeNodeServer();
        var lightServer = new FakeNodeServer();
        var heavy = MakeNode(heavyServer, "heavy");
        var light = MakeNode(lightServer, "light");
        extension.AddNode(heavy);
        extension.AddNode(light);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        var seen = new TaskCompletionSource<string>();
        extension.NodeStatsUpdated += (_, args) =>
        {
            seen.TrySetResult(args.Stats.PlayingPlayers.ToString());
            return Task.CompletedTask;
        };
        await heavyServer
            .PushAsync(
                """{"op":"stats","players":3,"playingPlayers":3,"uptime":500,"memory":{"free":1,"used":2,"allocated":3,"reservable":4},"cpu":{"cores":2,"systemLoad":0.1,"lavalinkLoad":0.2}}"""
            )
            .ConfigureAwait(false);
        await Assert
            .That(await seen.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            .IsEqualTo("3");

        await lightServer
            .PushAsync(
                """{"op":"stats","players":1,"playingPlayers":0,"uptime":500,"memory":{"free":1,"used":2,"allocated":3,"reservable":4},"cpu":{"cores":2,"systemLoad":0.1,"lavalinkLoad":0.2}}"""
            )
            .ConfigureAwait(false);
        await Task.Delay(200).ConfigureAwait(false);
        await Assert.That(ReferenceEquals(extension.GetNode(), light)).IsTrue();

        heavyServer.On(
            "PATCH",
            "/v4/sessions/fake-session/players/777",
            _ => new FakeRestResponse(
                200,
                """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
            )
        );
        var connect = heavy.ConnectAsync(777, 555);
        await Task.Delay(100).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        var conn = await connect.ConfigureAwait(false);
        await Assert.That(ReferenceEquals(extension.GetConnection(777), conn)).IsTrue();

        await conn.DisconnectAsync().ConfigureAwait(false);
        await extension.DisposeAsync().ConfigureAwait(false);
        heavyServer.Dispose();
        lightServer.Dispose();
    }

    [Test]
    public async Task ForwardErrorFloor()
    {
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var server = new FakeNodeServer();
        var node = MakeNode(server, "floor");
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        extension.NodeStatsUpdated += (_, _) =>
            throw new InvalidOperationException("the handler broke");
        var probe = new TaskCompletionSource<long>();
        node.StatsUpdated += (_, args) =>
        {
            probe.TrySetResult(args.Stats.Uptime);
            return Task.CompletedTask;
        };
        await server
            .PushAsync(
                """{"op":"stats","players":2,"playingPlayers":2,"uptime":900,"memory":{"free":1,"used":2,"allocated":3,"reservable":4},"cpu":{"cores":2,"systemLoad":0.1,"lavalinkLoad":0.2}}"""
            )
            .ConfigureAwait(false);
        await Assert
            .That(await probe.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            .IsEqualTo(900);

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task SyncDisposeBridge()
    {
        var bridge = new DisposableBridge();
        var extension = new NyraExtension(bridge);
        extension.Dispose();
        await Assert.That(bridge.DisposeCount).IsEqualTo(1);
        await Assert.That(extension.IsDisposed).IsTrue();
        extension.Dispose();
        await Assert.That(bridge.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task RemoveNodeFlow()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = MakeNode(server, "removable");
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();

        var foreign = MakeNode(server, "foreign");
        await extension.RemoveNodeAsync(foreign).ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();

        await extension.RemoveNodeAsync(node).ConfigureAwait(false);
        await Assert.That(node.IsConnected).IsFalse();
        bool readded;
        try
        {
            extension.AddNode(node);
            readded = true;
            extension.RemoveNodeAsync(node).Wait(2000);
        }
        catch (InvalidOperationException)
        {
            readded = false;
        }
        await Assert.That(readded).IsTrue();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task DisposedMidConnect()
    {
        var bridge = new NeverReadyBridge();
        var extension = new NyraExtension(bridge);
        var server = new FakeNodeServer();
        extension.AddNode(MakeNode(server, "parked"));
        var connecting = extension.ConnectAllNodesAsync();
        await Task.Delay(200).ConfigureAwait(false);
        extension.Dispose();
        await Assert
            .That(async () => await connecting.ConfigureAwait(false))
            .Throws<ObjectDisposedException>();
        server.Dispose();
    }

    [Test]
    public async Task BridgeDisposeShapes()
    {
        var throwing = new ThrowingSyncBridge();
        var throwingExtension = new NyraExtension(throwing);
        await Assert
            .That(() =>
            {
                throwingExtension.Dispose();
            })
            .Throws<InvalidOperationException>()
            .WithMessageContaining("the bridge broke on dispose");
        await Assert.That(throwing.Threw).IsTrue();

        var asyncOnly = new AsyncOnlyBridge();
        var asyncExtension = new NyraExtension(asyncOnly);
        await asyncExtension.DisposeAsync().ConfigureAwait(false);
        await Assert.That(asyncOnly.Disposed).IsTrue();

        var asyncOnly2 = new AsyncOnlyBridge();
        var syncExtension = new NyraExtension(asyncOnly2);
        syncExtension.Dispose();
        await Assert.That(asyncOnly2.Disposed).IsTrue();
    }

    private sealed class ThrowingLogger : ILogger
    {
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => throw new InvalidOperationException("the logger is down");

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }

    [Test]
    public async Task DisposeDuringConnectWithThrowingLogger()
    {
        var bridge = new NeverReadyBridge { Logger = new ThrowingLogger() };
        var extension = new NyraExtension(bridge);
        var server = new FakeNodeServer();
        extension.AddNode(MakeNode(server, "parked"));
        var connecting = extension.ConnectAllNodesAsync();
        await Task.Delay(200).ConfigureAwait(false);
        extension.Dispose();
        await Assert
            .That(async () => await connecting.ConfigureAwait(false))
            .Throws<ObjectDisposedException>();
        server.Dispose();
    }

    [Test]
    public async Task CancelledConnectRethrows()
    {
        var bridge = new NeverReadyBridge();
        var extension = new NyraExtension(bridge);
        var server = new FakeNodeServer();
        var node = MakeNode(server, "parked");
        extension.AddNode(node);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert
            .That(async () =>
                await extension.ConnectAllNodesAsync(cancelled.Token).ConfigureAwait(false)
            )
            .Throws<OperationCanceledException>();
        await Assert.That(extension.IsDisposed).IsFalse();
        await Assert.That(node.IsConnected).IsFalse();
        extension.Dispose();
        server.Dispose();
    }

    [Test]
    public async Task CancelledNodeConnectRethrows()
    {
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        using var server = new SilentServer();
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, "pass"),
            new NodeOptions
            {
                Name = "silent",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(8),
            }
        );
        extension.AddNode(node);
        using var cancelled = new CancellationTokenSource(150);
        await Assert
            .That(async () =>
            {
                await extension.ConnectAllNodesAsync(cancelled.Token).ConfigureAwait(false);
            })
            .Throws<OperationCanceledException>();
        await Assert.That(node.IsReady).IsFalse();
        extension.Dispose();
    }

    [Test]
    public async Task HostileLoggerDisposeFault()
    {
        var bridge = new ThrowingSyncBridge { Logger = new ThrowingLogger() };
        var extension = new NyraExtension(bridge);
        await Assert
            .That(() => extension.Dispose())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("the bridge broke on dispose");
        await Assert.That(extension.IsDisposed).IsTrue();
    }
}
