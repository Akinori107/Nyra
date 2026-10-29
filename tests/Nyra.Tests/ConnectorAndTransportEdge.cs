using System.Text;
using System.Text.Json;
using Nyra.Abstractions;
using Nyra.Entities;
using Nyra.Exceptions;
using Nyra.Internal;
using Nyra.WebSocket;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class ConnectorAndTransportEdge
{
    [Test]
    public async Task JsonNullable()
    {
        var options = NyraJson.SerializerOptions;

        using var snowflakeStream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(snowflakeStream))
            new NullableSnowflakeConverter().Write(writer, null, options);
        await Assert.That(Encoding.UTF8.GetString(snowflakeStream.ToArray())).IsEqualTo("null");

        var stringReader = new Utf8JsonReader("null"u8);
        stringReader.Read();
        await Assert
            .That(new StringOrNumberConverter().Read(ref stringReader, typeof(string), options))
            .IsNull();
        using var stringStream = new MemoryStream();
        using (var writer2 = new Utf8JsonWriter(stringStream))
            new StringOrNumberConverter().Write(writer2, null, options);
        await Assert.That(Encoding.UTF8.GetString(stringStream.ToArray())).IsEqualTo("null");

        var tolerantReader = new Utf8JsonReader("null"u8);
        tolerantReader.Read();
        await Assert
            .That(
                new NullableTolerantStringConverter().Read(
                    ref tolerantReader,
                    typeof(string),
                    options
                )
            )
            .IsNull();
        using var tolerantStream = new MemoryStream();
        using (var writer3 = new Utf8JsonWriter(tolerantStream))
            new NullableTolerantStringConverter().Write(writer3, null, options);
        await Assert.That(Encoding.UTF8.GetString(tolerantStream.ToArray())).IsEqualTo("null");

        using var boolStream = new MemoryStream();
        using (var writer4 = new Utf8JsonWriter(boolStream))
            new NullableTolerantBooleanConverter().Write(writer4, null, options);
        await Assert.That(Encoding.UTF8.GetString(boolStream.ToArray())).IsEqualTo("null");
    }

    [Test]
    public async Task PluginFilterComparer()
    {
        var other = JsonDocument.Parse("2").RootElement;

        var withPlugins = new FilterChain
        {
            PluginFilters = new Dictionary<string, JsonElement> { ["party"] = PluginRootElement() },
        };
        var bare = new FilterChain();

        static JsonElement PluginRootElement() =>
            JsonDocument.Parse("""{"amount":1}""").RootElement;

        await Assert.That(FilterComparer.FiltersEqual(bare, withPlugins)).IsFalse();

        var differentValue = new FilterChain
        {
            PluginFilters = new Dictionary<string, JsonElement>
            {
                ["party"] = JsonDocument.Parse("""{"amount":2}""").RootElement,
            },
        };
        await Assert.That(FilterComparer.FiltersEqual(withPlugins, differentValue)).IsFalse();

        var missingName = new FilterChain
        {
            PluginFilters = new Dictionary<string, JsonElement>
            {
                ["other"] = JsonDocument.Parse("""{"amount":1}""").RootElement,
            },
        };
        await Assert.That(FilterComparer.FiltersEqual(withPlugins, missingName)).IsFalse();

        _ = other;
    }

    [Test]
    public async Task MissingSessionRawFallback()
    {
        var arrayBody = new RestException(404, "/v4/x", "[1,2]", null);
        await Assert.That(RestClient.IndicatesMissingSession(arrayBody)).IsFalse();

        var sessionStringBody = new RestException(404, "/v4/x", "\"session\"", null);
        await Assert.That(RestClient.IndicatesMissingSession(sessionStringBody)).IsTrue();

        var quietObjectBody = new RestException(
            404,
            "/v4/x",
            """{"message":"player gone"}""",
            null
        );
        await Assert.That(RestClient.IndicatesMissingSession(quietObjectBody)).IsFalse();
    }

    [Test]
    public async Task WsDoubleStopWithoutConnect()
    {
        using var client = new WebSocketClient();
        await Assert.That(client.IsConnected).IsFalse();
        await client.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await client.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await Assert.That(client.IsConnected).IsFalse();
    }

    private static async Task<Stack> StartAsync(FakeNodeServer? server = null)
    {
        server ??= new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "transport-edge",
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
    public async Task RestTruncatedErrorBody()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            stack.Server.OnRaw(
                "GET",
                "/v4/sessions/fake-session/groups",
                async (_, stream) =>
                {
                    var head =
                        "HTTP/1.1 404 Not Found\r\nContent-Type: application/json\r\n"
                        + "Content-Length: 100\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(head)).ConfigureAwait(false);
                    await stream.WriteAsync(Encoding.UTF8.GetBytes("short")).ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                    stream.Close();
                }
            );
            var truncated = await Assert
                .That(async () =>
                    await stack
                        .Node.Rest.Groups.ListAsync(CancellationToken.None)
                        .ConfigureAwait(false)
                )
                .Throws<RestException>();
            await Assert.That(truncated!.StatusCode).IsEqualTo(404);
            await Assert.That(truncated!.ResponseBody).IsEqualTo(string.Empty);
        }
    }

    [Test]
    public async Task RestTruncatedSuccessBody()
    {
        var stack = await StartAsync().ConfigureAwait(false);
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
                        .WriteAsync(Encoding.UTF8.GetBytes("{\"half\":\"of the payload"))
                        .ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                    stream.Close();
                }
            );
            var unreachable = await Assert
                .That(async () =>
                    await stack
                        .Node.Rest.Groups.ListAsync(CancellationToken.None)
                        .ConfigureAwait(false)
                )
                .Throws<NodeUnreachableException>()
                .WithMessageContaining("transport-edge");
            await Assert
                .That(unreachable!.InnerException is IOException or HttpRequestException)
                .IsTrue();
        }
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
    public async Task NodeSkipWithHostileLogger()
    {
        var bridge = new FakeBridge { Logger = new HostileLogger() };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", DeadPort(), "unused"),
            new NodeOptions
            {
                Name = "transport-edge-dead",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                MaxReconnectAttempts = 0,
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert.That(node.IsConnected).IsFalse();
        await Assert.That(node.IsReady).IsFalse();
        await Assert.That(extension.ContainsNode(node)).IsTrue();
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
            .That(async () => await ((IAsyncDisposable)bridge).DisposeAsync().ConfigureAwait(false))
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
    public async Task HandshakeDoublePending()
    {
        var bridge = new FakeBridge();
        var coordinator = new VoiceHandshakeCoordinator(TimeSpan.FromSeconds(2));
        var first = coordinator.WaitForUpdatesAsync(777, 555, bridge, CancellationToken.None);
        await Task.Delay(100).ConfigureAwait(false);
        await Assert
            .That(async () =>
                await coordinator
                    .WaitForUpdatesAsync(777, 556, bridge, CancellationToken.None)
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>();
        try
        {
            await first.ConfigureAwait(false);
        }
        catch (VoiceHandshakeException) { }
    }

    [Test]
    public async Task HandshakeFaultedSend()
    {
        var bridge = new FakeBridge { FaultVoiceSends = true };
        var coordinator = new VoiceHandshakeCoordinator(TimeSpan.FromSeconds(5));
        var refused = await Assert
            .That(async () =>
                await coordinator
                    .WaitForUpdatesAsync(888, 555, bridge, CancellationToken.None)
                    .ConfigureAwait(false)
            )
            .Throws<InvalidOperationException>()
            .WithMessageContaining("refuses the send");
        await Assert.That(refused!.Message).Contains("refuses the send");
    }

    [Test]
    public async Task HandshakeSecondEchoSkip()
    {
        var bridge = new FakeBridge();
        var coordinator = new VoiceHandshakeCoordinator(TimeSpan.FromSeconds(2));
        var pending = coordinator.WaitForUpdatesAsync(999, 555, bridge, CancellationToken.None);
        await Task.Delay(100).ConfigureAwait(false);
        var firstEcho = coordinator.HandleVoiceStateUpdate(
            bridge,
            new VoiceStateUpdatedEventArgs(999, new DiscordVoiceState(null, "vsess"))
        );
        var secondEcho = coordinator.HandleVoiceStateUpdate(
            bridge,
            new VoiceStateUpdatedEventArgs(999, new DiscordVoiceState(null, "vsess"))
        );
        await Assert.That(firstEcho).IsTrue();
        await Assert.That(secondEcho).IsTrue();
        await Assert
            .That(async () => await pending.ConfigureAwait(false))
            .Throws<VoiceHandshakeException>();
    }

    [Test]
    public async Task LoadStreamBodyWall()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            stack.Server.OnRaw(
                "GET",
                "/v4/loadstream",
                async (_, stream) =>
                {
                    var head =
                        "HTTP/1.1 404 Not Found\r\nContent-Type: application/json\r\n"
                        + "Content-Length: 500\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(head)).ConfigureAwait(false);
                    await stream
                        .WriteAsync(Encoding.UTF8.GetBytes("{\"message\":"))
                        .ConfigureAwait(false);
                    await stream.FlushAsync().ConfigureAwait(false);
                    Thread.Sleep(61000);
                    stream.Close();
                }
            );
            await Assert
                .That(async () =>
                    await stack
                        .Node.Rest.Tracks.OpenLoadStreamAsync(
                            "track",
                            100,
                            0,
                            null,
                            LoadStreamFormat.Raw,
                            CancellationToken.None
                        )
                        .ConfigureAwait(false)
                )
                .Throws<TimeoutException>();
        }
    }

    [Test]
    public async Task GatewayWallHostileLogger()
    {
        var bridge = new NeverReadyBridge { Logger = new HostileLogger() };
        var extension = new NyraExtension(bridge);
        await Assert
            .That(async () => await extension.ConnectAllNodesAsync().ConfigureAwait(false))
            .Throws<TimeoutException>();
        await extension.DisposeAsync().ConfigureAwait(false);
    }
}
