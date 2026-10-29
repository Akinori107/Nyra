using Nyra.Abstractions;

namespace Nyra.Tests;

public sealed class GuildRemoval
{
    private sealed class HostileDebugLogger : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => throw new InvalidOperationException("the leave logger bit back");
    }

    [Test]
    public async Task HostileLeaveLoggerStillTearsDown()
    {
        var server = new FakeNodeServer();
        var bridge = new RemovableBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = await MakeNodeAsync(extension, server, "removal-hostile").ConfigureAwait(false);

        server.On(
            "PATCH",
            "/v4/sessions/fake-session/players/777",
            _ => new FakeRestResponse(
                200,
                """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
            )
        );

        var recorder = new RecordingLogger();
        bridge.Logger = recorder;
        var connect = node.ConnectAsync(777, 555);
        await Task.Delay(100).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        var conn = await connect.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await Assert.That(conn.IsConnected).IsTrue();

        bridge.FireVoiceState(777, null, "vsess");
        var left = false;
        for (var i = 0; i < 80 && !left; i++)
        {
            await Task.Delay(25).ConfigureAwait(false);
            left = node.GetConnection(777) is null;
        }
        await Assert.That(left).IsTrue();

        bridge.Logger = new HostileDebugLogger();
        var reconnect = node.ConnectAsync(777, 555);
        await Task.Delay(100).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess2");
        bridge.FireVoiceServer(777, "tok2", "127.0.0.1");
        var second = await reconnect.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await Assert.That(second.IsConnected).IsTrue();

        bridge.FireVoiceState(777, null, "vsess2");
        var removed = false;
        for (var i = 0; i < 80 && !removed; i++)
        {
            await Task.Delay(25).ConfigureAwait(false);
            removed = node.GetConnection(777) is null;
        }
        await Assert.That(removed).IsTrue();
        await Assert.That(second.IsConnected).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    private sealed class RemovableBridge : IDiscordBridge, IGuildRemovalSource
    {
        private readonly TaskCompletionSource<ClientInformation> _ready = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public event EventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated;

        public event EventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated;

        public event EventHandler<GuildRemovedEventArgs>? GuildRemoved;

        public Microsoft.Extensions.Logging.ILogger? Logger { get; set; }

        public List<(ulong GuildId, ulong? ChannelId)> VoiceSends { get; } = [];

        public void GoReady() => _ready.TrySetResult(new ClientInformation(FakeBridge.UserId));

        public Task<ClientInformation> WaitForReadyAsync(
            CancellationToken cancellationToken = default
        ) => _ready.Task.WaitAsync(cancellationToken);

        public Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        )
        {
            VoiceSends.Add((guildId, channelId));
            return Task.CompletedTask;
        }

        public void FireVoiceState(ulong guildId, ulong? channelId, string? sessionId) =>
            VoiceStateUpdated?.Invoke(
                this,
                new VoiceStateUpdatedEventArgs(guildId, new DiscordVoiceState(channelId, sessionId))
            );

        public void FireVoiceServer(ulong guildId, string token, string endpoint) =>
            VoiceServerUpdated?.Invoke(
                this,
                new VoiceServerUpdatedEventArgs(guildId, new VoiceServer(token, endpoint))
            );

        public void FireGuildRemoved(ulong guildId) =>
            GuildRemoved?.Invoke(this, new GuildRemovedEventArgs(guildId));
    }

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

    private static async Task<Node> MakeNodeAsync(
        NyraExtension extension,
        FakeNodeServer server,
        string name
    )
    {
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = name,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                MaxReconnectAttempts = 2,
                ReadyTimeout = TimeSpan.FromSeconds(5),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return node;
    }

    [Test]
    public async Task RemovalEndsTheConnection()
    {
        var server = new FakeNodeServer();
        var bridge = new RemovableBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = await MakeNodeAsync(extension, server, "removal").ConfigureAwait(false);

        server.On(
            "PATCH",
            "/v4/sessions/fake-session/players/777",
            _ => new FakeRestResponse(
                200,
                """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
            )
        );
        var connect = node.ConnectAsync(777, 555);
        await Task.Delay(100).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        var conn = await connect.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await Assert.That(conn.IsConnected).IsTrue();

        bridge.FireGuildRemoved(777);
        var removed = false;
        for (var i = 0; i < 80 && !removed; i++)
        {
            await Task.Delay(25).ConfigureAwait(false);
            removed = node.GetConnection(777) is null;
        }
        await Assert.That(removed).IsTrue();
        await Assert.That(conn.IsConnected).IsFalse();
        await Assert.That(!bridge.VoiceSends.Contains((777, null))).IsTrue();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
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
