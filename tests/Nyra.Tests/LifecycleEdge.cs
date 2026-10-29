using DSharpPlus.Entities;
using NetCord;
using NetCord.Gateway;
using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;
using DspClient = DSharpPlus.DiscordClient;

namespace Nyra.Tests;

public sealed class LifecycleEdge
{
    private const string GhostPath = "/v4/sessions/fake-session/players/888";

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
                Name = "lifecycle-edge",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
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
    public async Task DspAdapterGuards()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        try
        {
            var channel = Reflect.New<DiscordChannel>();
            Reflect.Set(channel, "Type", global::DSharpPlus.ChannelType.Text);
            Reflect.Set(channel, "GuildId", (ulong?)777);

            var nulls = await Assert
                .That(() =>
                {
                    _ = Nyra.DSharpPlus.NodeExtensions.ConnectAsync(null!, channel);
                })
                .Throws<ArgumentNullException>();
            await Assert.That(nulls!.ParamName).IsEqualTo("node");

            var noChannel = await Assert
                .That(() =>
                {
                    _ = Nyra.DSharpPlus.NodeExtensions.ConnectAsync(stack.Node, null!);
                })
                .Throws<ArgumentNullException>();
            await Assert.That(noChannel!.ParamName).IsEqualTo("channel");

            await Assert
                .That(() =>
                {
                    _ = Nyra.DSharpPlus.NodeExtensions.ConnectAsync(stack.Node, channel);
                })
                .Throws<ArgumentException>();

            Reflect.Set(channel, "Type", global::DSharpPlus.ChannelType.Voice);
            Reflect.Set(channel, "GuildId", (ulong?)null);
            await Assert
                .That(() =>
                {
                    _ = Nyra.DSharpPlus.NodeExtensions.ConnectAsync(stack.Node, channel);
                })
                .Throws<ArgumentException>();

            Reflect.Set(channel, "Id", (ulong)555);
            Reflect.Set(channel, "GuildId", (ulong?)777);
            stack.Server.On(
                "PATCH",
                PlayerPath,
                _ => new FakeRestResponse(
                    200,
                    """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
                )
            );
            var join = Nyra.DSharpPlus.NodeExtensions.ConnectAsync(stack.Node, channel);
            await Task.Delay(100).ConfigureAwait(false);
            stack.Bridge.FireVoiceState(777, 555, "vsess");
            stack.Bridge.FireVoiceServer(777, "tok", "127.0.0.1");
            var forwarded = await join.ConfigureAwait(false);
            await Assert.That(forwarded.GuildId).IsEqualTo(777);
        }
        finally
        {
            await stack.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task RestError()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        try
        {
            stack.Server.On("GET", "/v4/decodetrack", 500, "the node exploded");
            var failed = await Assert
                .That(async () =>
                    await stack
                        .Node.Rest.Tracks.DecodeAsync("anything", CancellationToken.None)
                        .ConfigureAwait(false)
                )
                .Throws<RestException>()
                .WithMessageContaining("the node exploded");
            await Assert.That(failed!.StatusCode).IsEqualTo(500);

            stack.Server.On(
                "GET",
                "/v4/decodetrack",
                _ => new FakeRestResponse(429, """{"timestamp":0,"status":429}""", "250")
            );
            var throttled = await Assert
                .That(async () =>
                    await stack
                        .Node.Rest.Tracks.DecodeAsync("anything", CancellationToken.None)
                        .ConfigureAwait(false)
                )
                .Throws<RestException>();
            await Assert.That(throttled!.StatusCode).IsEqualTo(429);
            await Assert.That(throttled!.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(250));
        }
        finally
        {
            await stack.DisposeAsync().ConfigureAwait(false);
        }

        var deadBridge = new FakeBridge();
        deadBridge.GoReady();
        var deadExtension = new NyraExtension(deadBridge);
        var deadNode = new Node(
            new Endpoint("127.0.0.1", 1, "youshallnotpass"),
            new NodeOptions { Name = "dead-port", ReadyTimeout = TimeSpan.FromSeconds(2) }
        );
        deadExtension.AddNode(deadNode);
        await Assert
            .That(async () =>
                await deadNode
                    .Rest.Tracks.DecodeAsync("anything", CancellationToken.None)
                    .ConfigureAwait(false)
            )
            .Throws<NodeUnreachableException>();
        await deadExtension.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class SyncDisposeBridge : FakeBridge, IDisposable
    {
        public bool Disposed;

        public void Dispose() => Disposed = true;
    }

    private sealed class ThrowingSyncBridge : FakeBridge, IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("sync dispose boom");
    }

    [Test]
    public async Task BridgeDispose()
    {
        var server = new FakeNodeServer();
        var bridge = new SyncDisposeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions { Name = "sync-dispose", ReadyTimeout = TimeSpan.FromSeconds(5) }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        extension.Dispose();
        await Assert.That(bridge.Disposed).IsTrue();
        server.Dispose();

        var server2 = new FakeNodeServer();
        var bridge2 = new ThrowingSyncBridge();
        bridge2.GoReady();
        var extension2 = new NyraExtension(bridge2);
        var node2 = new Node(
            new Endpoint("127.0.0.1", server2.Port, server2.Password),
            new NodeOptions { Name = "throwing-dispose", ReadyTimeout = TimeSpan.FromSeconds(5) }
        );
        extension2.AddNode(node2);
        await extension2.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert
            .That(() => extension2.Dispose())
            .Throws<InvalidOperationException>()
            .WithMessageContaining("sync dispose boom");
        server2.Dispose();
    }

    [Test]
    public async Task ReadyHostileSession()
    {
        var server = new FakeNodeServer { SuppressReady = true };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "hostile-ready",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        var connectAll = extension.ConnectAllNodesAsync();
        await Task.Delay(300).ConfigureAwait(false);
        await server
            .PushAsync("""{"op":"ready","resumed":false,"sessionId":null}""")
            .ConfigureAwait(false);
        await connectAll.ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();
        await Assert.That(node.SessionId).IsNull();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();

        var hostile = new FakeNodeServer();
        hostile.On("PATCH", "/v4/sessions/fake-session", 500, "session update exploded");
        var hostileBridge = new FakeBridge();
        hostileBridge.GoReady();
        var hostileExtension = new NyraExtension(hostileBridge);
        var hostileNode = new Node(
            new Endpoint("127.0.0.1", hostile.Port, hostile.Password),
            new NodeOptions
            {
                Name = "hostile-update",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        var reported = new TaskCompletionSource<bool>();
        hostileNode.Error += (_, _) =>
        {
            reported.TrySetResult(true);
            return Task.CompletedTask;
        };
        hostileExtension.AddNode(hostileNode);
        await hostileExtension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert.That(hostileNode.IsReady).IsTrue();
        var sawFailure = false;
        for (var i = 0; i < 30 && !sawFailure; i++)
        {
            await Task.Delay(100).ConfigureAwait(false);
            sawFailure = reported.Task.IsCompleted;
        }
        await Assert.That(sawFailure).IsTrue();
        await hostileExtension.DisposeAsync().ConfigureAwait(false);
        hostile.Dispose();
    }

    [Test]
    public async Task StatsBeforeTheReadyAreRaised()
    {
        var server = new FakeNodeServer { SuppressReady = true };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "stats-buffer",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        var order = new List<string>();
        var lateStats = new TaskCompletionSource<Nyra.EventArgs.NodeStatsEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        node.Ready += (_, _) =>
        {
            lock (order)
                order.Add("ready");
            return Task.CompletedTask;
        };
        node.StatsUpdated += (_, args) =>
        {
            lock (order)
                order.Add("stats");
            lateStats.TrySetResult(args);
            return Task.CompletedTask;
        };
        var connectAll = extension.ConnectAllNodesAsync();
        await Task.Delay(300).ConfigureAwait(false);
        await server
            .PushAsync(
                """{"op":"stats","players":1,"playingPlayers":1,"uptime":500,"memory":{"free":1,"used":2,"allocated":3,"reservable":4},"cpu":{"cores":2,"systemLoad":0.1,"lavalinkLoad":0.2}}"""
            )
            .ConfigureAwait(false);
        await Task.Delay(200).ConfigureAwait(false);
        await server
            .PushAsync("""{"op":"ready","resumed":false,"sessionId":"fake-session"}""")
            .ConfigureAwait(false);
        await connectAll.ConfigureAwait(false);
        var raised = await lateStats.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await Assert.That(raised.Stats.Players).IsEqualTo(1);
        await Assert.That(raised.Stats.Uptime).IsEqualTo(500);
        await Assert
            .That(() => order.Count == 2)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        await Assert
            .That(order.ToArray())
            .IsEquivalentTo(["stats", "ready"], TUnit.Assertions.Enums.CollectionOrdering.Matching);

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task OrphanSweep()
    {
        var destroyed = new List<string>();
        var mode = 0;
        var stack = await StartAsync().ConfigureAwait(false);
        try
        {
            await ConnectGuildAsync(stack.Bridge, stack.Node, stack.Server).ConfigureAwait(false);
            stack.Server.On(
                "GET",
                "/v4/sessions/fake-session/players",
                _ => new FakeRestResponse(
                    200,
                    """[{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}},{"guildId":"888","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}]"""
                )
            );
            stack.Server.On(
                "DELETE",
                GhostPath,
                _ =>
                {
                    destroyed.Add("888");
                    return mode switch
                    {
                        1 => new FakeRestResponse(404, string.Empty),
                        2 => new FakeRestResponse(500, "destroy exploded"),
                        _ => new FakeRestResponse(204, string.Empty),
                    };
                }
            );

            await stack.Server.DropConnectionAsync(1000, "orphan test drop").ConfigureAwait(false);
            var resumedAgain = new TaskCompletionSource<bool>();
            stack.Node.Connected += (_, args) =>
            {
                resumedAgain.TrySetResult(true);
                return Task.CompletedTask;
            };
            await resumedAgain.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            for (var i = 0; i < 40 && destroyed.Count == 0; i++)
                await Task.Delay(100).ConfigureAwait(false);
            await Assert.That(destroyed.Count).IsEqualTo(1);

            mode = 1;
            await stack.Server.DropConnectionAsync(1000, "orphan 404 drop").ConfigureAwait(false);
            await Task.Delay(1500).ConfigureAwait(false);
            await Assert.That(stack.Node.IsReady).IsTrue();

            mode = 2;
            await stack.Server.DropConnectionAsync(1000, "orphan 500 drop").ConfigureAwait(false);
            await Task.Delay(1500).ConfigureAwait(false);
            await Assert.That(stack.Node.IsReady).IsTrue();
            await Assert.That(destroyed.Count).IsEqualTo(3);
        }
        finally
        {
            await stack.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ConnectNodeThrow()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions { Name = "canceled-connect", ReadyTimeout = TimeSpan.FromSeconds(5) }
        );
        extension.AddNode(node);
        var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert
            .That(async () =>
                await extension.ConnectAllNodesAsync(canceled.Token).ConfigureAwait(false)
            )
            .Throws<OperationCanceledException>();
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();

        var server2 = new FakeNodeServer();
        var bridge2 = new FakeBridge();
        bridge2.GoReady();
        var extension2 = new NyraExtension(bridge2);
        await extension2.DisposeAsync().ConfigureAwait(false);
        var late = new Node(
            new Endpoint("127.0.0.1", server2.Port, server2.Password),
            new NodeOptions { Name = "late-node", ReadyTimeout = TimeSpan.FromSeconds(5) }
        );
        await Assert
            .That(async () =>
            {
                extension2.AddNode(late);
                await extension2.ConnectAllNodesAsync().ConfigureAwait(false);
            })
            .Throws<ObjectDisposedException>();
        server2.Dispose();
    }

    [Test]
    public async Task LoadStream()
    {
        var deadBridge = new FakeBridge();
        deadBridge.GoReady();
        var deadExtension = new NyraExtension(deadBridge);
        var deadNode = new Node(
            new Endpoint("127.0.0.1", 1, "youshallnotpass"),
            new NodeOptions { Name = "dead-stream", ReadyTimeout = TimeSpan.FromSeconds(2) }
        );
        deadExtension.AddNode(deadNode);
        await Assert
            .That(async () =>
                await deadNode
                    .Rest.Tracks.OpenLoadStreamAsync(
                        "track",
                        100,
                        0,
                        null,
                        LoadStreamFormat.Raw,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false)
            )
            .Throws<NodeUnreachableException>();
        await deadExtension.DisposeAsync().ConfigureAwait(false);

        var stack = await StartAsync().ConfigureAwait(false);
        try
        {
            stack.Server.On("GET", "/v4/loadstream", 500, "the stream exploded");
            var exploded = await Assert
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
                .Throws<RestException>()
                .WithMessageContaining("the stream exploded");
            await Assert.That(exploded!.StatusCode).IsEqualTo(500);
        }
        finally
        {
            await stack.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task FilterResendEcho()
    {
        const string divergentEcho =
            """{"guildId":"777","track":null,"volume":100,"paused":false,"filters":{"timescale":{"speed":2.0,"rate":1.0,"pitch":1.0}},"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}""";
        const string trackStart =
            """{"op":"event","guildId":"777","type":"TrackStartEvent","track":null}""";
        static int Patches(FakeNodeServer server) =>
            server.RestCalls.Count(call =>
                call.Method == "PATCH"
                && call.Path == PlayerPath
                && call.Body is { } body
                && body.Contains("\"speed\":1.5", StringComparison.Ordinal)
            );

        var stack = await StartAsync().ConfigureAwait(false);
        try
        {
            var connection = await ConnectGuildAsync(stack.Bridge, stack.Node, stack.Server)
                .ConfigureAwait(false);
            stack.Server.On("PATCH", PlayerPath, _ => new FakeRestResponse(200, divergentEcho));
            await connection
                .Filters.AddAsync(new Nyra.Entities.Filters.TimescaleFilter { Speed = 1.5 })
                .ConfigureAwait(false);
            await Assert.That(Patches(stack.Server)).IsEqualTo(1);

            await stack.Server.PushAsync(trackStart).ConfigureAwait(false);
            await Assert
                .That(() => Patches(stack.Server) == 2)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Assert.That(stack.Node.IsReady).IsTrue();

            var second = await StartAsync().ConfigureAwait(false);
            try
            {
                var guild = await ConnectGuildAsync(second.Bridge, second.Node, second.Server)
                    .ConfigureAwait(false);
                var parked = new TaskCompletionSource<Exception>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                second.Node.Error += (_, args) =>
                {
                    parked.TrySetResult(args.Exception);
                    return Task.CompletedTask;
                };
                second.Server.On(
                    "PATCH",
                    PlayerPath,
                    _ => new FakeRestResponse(200, divergentEcho)
                );
                await guild
                    .Filters.AddAsync(new Nyra.Entities.Filters.TimescaleFilter { Speed = 1.5 })
                    .ConfigureAwait(false);
                second.Server.On(
                    "PATCH",
                    PlayerPath,
                    _ => new FakeRestResponse(429, """{"timestamp":0,"status":429}""", "30")
                );
                await second.Server.PushAsync(trackStart).ConfigureAwait(false);
                var reported = await parked
                    .Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                await Assert.That(reported.InnerException).IsTypeOf<RestException>();
                var afterParking = Patches(second.Server);

                await second.Server.PushAsync(trackStart).ConfigureAwait(false);
                await Task.Delay(500).ConfigureAwait(false);
                await Assert.That(Patches(second.Server)).IsEqualTo(afterParking);
                await Assert.That(second.Node.IsReady).IsTrue();
            }
            finally
            {
                await second.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await stack.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class SlowAsyncBridge : FakeBridge, IAsyncDisposable
    {
        public bool Slow;

        public new ValueTask DisposeAsync()
        {
            if (Slow)
                return new ValueTask(Task.Delay(TimeSpan.FromSeconds(5)));
            return base.DisposeAsync();
        }
    }

    [Test]
    public async Task DisposeSlow()
    {
        var server = new FakeNodeServer();
        var bridge = new SlowAsyncBridge { Slow = true };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions { Name = "slow-bridge", ReadyTimeout = TimeSpan.FromSeconds(5) }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        extension.Dispose();
        watch.Stop();
        server.Dispose();
        await Assert.That(watch.Elapsed).IsGreaterThan(TimeSpan.FromSeconds(2.5));
        await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));

        var busyServer = new FakeNodeServer();
        var busyBridge = new FakeBridge();
        busyBridge.GoReady();
        var busyExtension = new NyraExtension(busyBridge);
        var busyNode = new Node(
            new Endpoint("127.0.0.1", busyServer.Port, busyServer.Password),
            new NodeOptions
            {
                Name = "busy-drain",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        busyExtension.AddNode(busyNode);
        await busyExtension.ConnectAllNodesAsync().ConfigureAwait(false);
        busyNode.Disconnected += async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        };
        await busyServer.DropConnectionAsync(1000, "busy drain drop").ConfigureAwait(false);
        await Task.Delay(300).ConfigureAwait(false);
        watch.Restart();
        busyExtension.Dispose();
        watch.Stop();
        busyServer.Dispose();
        await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(4.5));
    }

    [Test]
    public async Task SixtySecondWalls()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        try
        {
            stack.Server.On(
                "GET",
                "/v4/decodetrack",
                _ =>
                {
                    Thread.Sleep(61000);
                    return new FakeRestResponse(200, "{}");
                }
            );
            stack.Server.On(
                "GET",
                "/v4/loadstream",
                _ =>
                {
                    Thread.Sleep(61000);
                    return new FakeRestResponse(200, "audio");
                }
            );
            async Task Decode() =>
                await Assert
                    .That(async () =>
                        await stack
                            .Node.Rest.Tracks.DecodeAsync("anything", CancellationToken.None)
                            .ConfigureAwait(false)
                    )
                    .Throws<TimeoutException>();
            async Task Stream() =>
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
            await Task.WhenAll(Decode(), Stream()).ConfigureAwait(false);
        }
        finally
        {
            await stack.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class RecordingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public int Warnings;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                Warnings++;
        }
    }

    [Test]
    public async Task NetCordShardedWarning()
    {
        var token = new BotToken("MTIz.ab.cd");
        var shard = new GatewayClient(token);
        var registryField = typeof(Nyra.NetCord.GatewayClientExtensions).GetField(
            "Extensions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        var registry = await Assert
            .That(registryField?.GetValue(null))
            .IsTypeOf<System.Runtime.CompilerServices.ConditionalWeakTable<
                GatewayClient,
                NyraExtension
            >>();

        var stack = await StartAsync().ConfigureAwait(false);
        try
        {
            registry!.Remove(shard);
            registry.Add(shard, stack.Extension);

            var stateType =
                typeof(ShardedGatewayClient).GetNestedType(
                    "State",
                    System.Reflection.BindingFlags.NonPublic
                ) ?? throw new MissingMemberException("ShardedGatewayClient.State");
            var state = System.Activator.CreateInstance(stateType)!;
            stateType.GetProperty("Clients")!.SetValue(state, new[] { shard });
            var sharded = new ShardedGatewayClient(token);
            Reflect.Field(sharded, "_state", state);

            var logger = new RecordingLogger();
            var extension = Nyra.NetCord.GatewayClientExtensions.UseNyra(sharded, logger);
            await Assert.That(extension).IsNotNull();
            await Assert.That(extension!.IsDisposed).IsFalse();
            await Assert.That(logger.Warnings).IsGreaterThanOrEqualTo(1);
            if (extension is not null)
                await extension.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            await stack.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task DspRemove()
    {
        await Assert
            .That(() => Nyra.DSharpPlus.DiscordClientExtensions.RemoveNyra(null!))
            .Throws<ArgumentNullException>()
            .WithParameterName("client");

        var registryField = typeof(Nyra.DSharpPlus.DiscordClientExtensions).GetField(
            "Extensions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        var registry = await Assert
            .That(registryField?.GetValue(null))
            .IsTypeOf<System.Runtime.CompilerServices.ConditionalWeakTable<
                DspClient,
                NyraExtension
            >>();

        var stack = await StartAsync().ConfigureAwait(false);
        try
        {
            var dspClient = Reflect.New<DspClient>();
            registry!.Remove(dspClient);
            registry.Add(dspClient, stack.Extension);
            Nyra.DSharpPlus.DiscordClientExtensions.RemoveNyra(dspClient);
            await Assert.That(stack.Extension.IsDisposed).IsTrue();
        }
        finally
        {
            await stack.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task ReadyWatchdogAbort()
    {
        var server = new FakeNodeServer { SuppressReady = true };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "watchdog",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromMilliseconds(500),
            }
        );
        extension.AddNode(node);
        var connectAll = extension.ConnectAllNodesAsync();
        await Task.Delay(2500).ConfigureAwait(false);
        await Assert.That(node.IsReady).IsFalse();
        await Assert.That(server.WebsocketConnections).IsGreaterThanOrEqualTo(2);
        await extension.DisposeAsync().ConfigureAwait(false);
        try
        {
            await connectAll.ConfigureAwait(false);
        }
        catch (Exception) { }
        server.Dispose();
    }
}
