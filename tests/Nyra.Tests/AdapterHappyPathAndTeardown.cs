using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using DSharpPlus;
using DSharpPlus.AsyncEvents;
using DSharpPlus.EventArgs;
using DSharpPlus.Net.WebSocket;
using Microsoft.Extensions.Logging;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class AdapterHappyPathAndTeardown
{
    private const string PlayerJson =
        """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}""";

    private sealed class Stack : IAsyncDisposable
    {
        public required FakeBridge Bridge { get; init; }

        public required NyraExtension Extension { get; init; }

        public required Node Node { get; init; }

        public required FakeNodeServer Server { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private static async Task<Stack> StartAsync()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "adapter-teardown",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new Stack
        {
            Bridge = bridge,
            Extension = extension,
            Node = node,
            Server = server,
        };
    }

    private sealed class FakeDspSocket : global::DSharpPlus.Net.WebSocket.IWebSocketClient
    {
        public readonly List<string> Sent = [];

        public System.Net.IWebProxy Proxy => null!;

        public IReadOnlyDictionary<string, string> DefaultHeaders =>
            new Dictionary<string, string>();

        public Task ConnectAsync(Uri uri) => Task.CompletedTask;

        public Task DisconnectAsync(int code = 1000, string message = "") => Task.CompletedTask;

        public Task SendMessageAsync(string message)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }

        public bool AddDefaultHeader(string name, string value) => true;

        public bool RemoveDefaultHeader(string name) => true;

        public event AsyncEventHandler<IWebSocketClient, SocketEventArgs> Connected
        {
            add { }
            remove { }
        }

        public event AsyncEventHandler<IWebSocketClient, SocketCloseEventArgs> Disconnected
        {
            add { }
            remove { }
        }

        public event AsyncEventHandler<IWebSocketClient, SocketMessageEventArgs> MessageReceived
        {
            add { }
            remove { }
        }

        public event AsyncEventHandler<IWebSocketClient, SocketErrorEventArgs> ExceptionThrown
        {
            add { }
            remove { }
        }

        public void Dispose() { }
    }

    [Test]
    public async Task StableAdapterHappyPath()
    {
        var client = new DiscordClient(
            new DiscordConfiguration
            {
                Token = "MTIzNDU2.Nzg5.MDEy",
                Intents = DiscordIntents.Guilds | DiscordIntents.GuildVoiceStates,
            }
        );
        var socket = new FakeDspSocket();
        Reflect.Field(client, "_webSocketClient", socket);
        using var bridge = new Nyra.DSharpPlus.DSharpPlusBridge(client);
        await bridge
            .SendVoiceUpdateAsync(77, 555, false, false, CancellationToken.None)
            .ConfigureAwait(false);
        await Assert.That(socket.Sent.Count).IsEqualTo(1);
        await Assert.That(socket.Sent[0]).Contains("\"op\":4");
        await Assert.That(socket.Sent[0]).Contains("\"channel_id\":\"555\"");
    }

    private sealed class FakeNcConnection : global::NetCord.Gateway.WebSockets.IWebSocketConnection
    {
        public readonly List<string> Sent = [];

        public int? CloseStatus => null;

        public string? CloseStatusDescription => null;

        public ValueTask OpenAsync(Uri uri, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public void Abort() { }

        public ValueTask CloseAsync(
            int closeStatus,
            string? closeStatusDescription,
            CancellationToken cancellationToken = default
        ) => ValueTask.CompletedTask;

        public ValueTask<global::NetCord.Gateway.WebSockets.WebSocketConnectionReceiveResult> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("no inbound traffic in this fake");

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            global::NetCord.Gateway.WebSockets.WebSocketMessageType messageType,
            global::NetCord.Gateway.WebSockets.WebSocketMessageFlags messageFlags,
            CancellationToken cancellationToken = default
        )
        {
            Sent.Add(Encoding.UTF8.GetString(buffer.Span));
            return ValueTask.CompletedTask;
        }

        public void Dispose() { }
    }

    private sealed class FakeNcRateLimiter : global::NetCord.Gateway.IRateLimiter
    {
        public ValueTask<global::NetCord.Gateway.RateLimitAcquisitionResult> TryAcquireAsync(
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(global::NetCord.Gateway.RateLimitAcquisitionResult.NoRateLimit);

        public ValueTask CancelAcquireAsync(
            long acquisitionTimestamp,
            CancellationToken cancellationToken = default
        ) => ValueTask.CompletedTask;

        public void Dispose() { }
    }

    private static void SetByType(object target, Type interfaceType, object value)
    {
        foreach (
            var field in target
                .GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        )
        {
            if (field.FieldType == interfaceType)
            {
                field.SetValue(target, value);
                return;
            }
        }
        throw new MissingFieldException(
            $"no field of type {interfaceType.Name} on {target.GetType().Name}"
        );
    }

    [Test]
    public async Task NetCordHappyPath()
    {
        var client = new global::NetCord.Gateway.GatewayClient(
            new global::NetCord.BotToken("MTIz.ab.cd")
        );
        var connection = new FakeNcConnection();
        var limiter = new FakeNcRateLimiter();

        var assembly = typeof(global::NetCord.Gateway.GatewayClient).Assembly;
        var wsType = typeof(global::NetCord.Gateway.WebSocketClient);
        var stateType =
            wsType.GetNestedType("State", BindingFlags.NonPublic)
            ?? throw new MissingMemberException("WebSocketClient.State");
        var connectionStateType =
            wsType.GetNestedType("ConnectionState", BindingFlags.NonPublic)
            ?? throw new MissingMemberException("WebSocketClient.ConnectionState");
        var resultType =
            wsType.GetNestedType("ConnectionStateResult", BindingFlags.NonPublic)
            ?? throw new MissingMemberException("WebSocketClient.ConnectionStateResult");

        var fakeConnectionState = RuntimeHelpers.GetUninitializedObject(connectionStateType);
        SetByType(
            fakeConnectionState,
            typeof(global::NetCord.Gateway.WebSockets.IWebSocketConnection),
            connection
        );
        SetByType(fakeConnectionState, typeof(global::NetCord.Gateway.IRateLimiter), limiter);
        var providerType =
            assembly.GetType("NetCord.Gateway.CancellationTokenProvider")
            ?? throw new MissingMemberException("NetCord.Gateway.CancellationTokenProvider");
        Reflect.Field(
            fakeConnectionState,
            "<DisconnectedTokenProvider>k__BackingField",
            RuntimeHelpers.GetUninitializedObject(providerType)
        );

        var state = RuntimeHelpers.GetUninitializedObject(stateType);
        Reflect.Field(state, "_lock", new Lock());
        var stateProviderType =
            assembly.GetType("NetCord.Gateway.CancellationTokenProvider")
            ?? throw new MissingMemberException("NetCord.Gateway.CancellationTokenProvider");
        Reflect.Field(
            state,
            "<ClosedTokenProvider>k__BackingField",
            RuntimeHelpers.GetUninitializedObject(stateProviderType)
        );
        var completionSourceType = typeof(TaskCompletionSource<>).MakeGenericType(resultType);
        Reflect.Field(
            state,
            "_readyCompletionSource",
            Activator.CreateInstance(completionSourceType)!
        );
        Reflect.Field(state, "_connectionState", fakeConnectionState);

        Reflect.Field(client, "_state", state);
        stateType
            .GetMethod("IndicateReady", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(state, new object?[] { fakeConnectionState });

        using var bridge = new Nyra.NetCord.GatewayBridge(client, _ => { }, _ => { }, _ => { });
        await bridge
            .SendVoiceUpdateAsync(5, 77, false, false, CancellationToken.None)
            .ConfigureAwait(false);
        await Assert.That(connection.Sent.Count).IsEqualTo(1);
        await Assert.That(connection.Sent[0]).Contains("77");
    }

    [Test]
    public async Task RetryAfterFallThrough()
    {
        using var response = new HttpResponseMessage();
        response.Headers.RetryAfter = new RetryConditionHeaderValue(
            DateTimeOffset.UtcNow - TimeSpan.FromHours(1)
        );
        var read = Nyra.Internal.RestClient.ReadRetryAfter(response);
        await Assert.That(read).IsNull();
    }

    [Test]
    public async Task TrackStream()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            using var callerCts = new CancellationTokenSource();
            stack.Server.OnRaw(
                "GET",
                "/v4/loadstream",
                async (call, raw) =>
                {
                    callerCts.Cancel();
                    await Task.Delay(TimeSpan.FromSeconds(80)).ConfigureAwait(false);
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
                            default,
                            callerCts.Token
                        )
                        .ConfigureAwait(false)
                )
                .Throws<OperationCanceledException>();
        }
    }

    [Test]
    public async Task InfoSecondDocument()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            stack.Server.On("GET", "/v4/info", 200, "{} {}");
            var rejected = await Assert
                .That(async () =>
                    await stack
                        .Node.Rest.Info.GetServerInfoAsync(CancellationToken.None)
                        .ConfigureAwait(false)
                )
                .Throws<Nyra.Exceptions.IncompatibleServerException>();
            await Assert.That(rejected!.InnerException).IsTypeOf<System.Text.Json.JsonException>();
        }
    }

    [Test]
    public async Task ReconcilePausePoll()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            server.On("GET", PlayerPath, _ => new FakeRestResponse(200, PlayerJson));
            server.OnRaw(
                "PATCH",
                PlayerPath,
                async (call, raw) =>
                {
                    await raw.DisposeAsync().ConfigureAwait(false);
                }
            );

            await Assert
                .That(async () => await conn.PauseAsync().ConfigureAwait(false))
                .Throws<Nyra.Exceptions.NodeUnreachableException>();
            await Assert
                .That(server.RestCalls.Any(c => c is { Method: "GET", Path: PlayerPath }))
                .IsTrue();
        }
    }

    private sealed class SleepingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Thread.Sleep(60);
        }
    }
}
