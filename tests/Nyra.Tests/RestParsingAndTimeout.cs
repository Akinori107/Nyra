using System.Globalization;
using Microsoft.Extensions.Logging;
using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class RestParsingAndTimeout
{
    private const string Session = "fake-session";
    private const ulong Guild = 424242;

    private sealed record Stack : IAsyncDisposable
    {
        public required NyraExtension Extension { get; init; }

        public required Node Node { get; init; }

        public required FakeNodeServer Server { get; init; }

        public required CaptureLogger Logger { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private sealed class CaptureLogger : ILogger
    {
        public sealed record Entry(LogLevel Level, string Message, string? Exception);

        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel level,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (Entries)
                Entries.Add(new Entry(level, formatter(state, exception), exception?.Message));
        }

        public int Count
        {
            get
            {
                lock (Entries)
                    return Entries.Count;
            }
        }
    }

    private static async Task<Stack> StartAsync(
        FakeNodeServer? server = null,
        Action<NodeOptions>? tune = null
    )
    {
        server ??= new FakeNodeServer();
        var logger = new CaptureLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var options = new NodeOptions
        {
            Name = "rest-parsing",
            Resuming = true,
            ReconnectDelay = TimeSpan.FromMilliseconds(50),
            MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
            VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
            ReadyTimeout = TimeSpan.FromSeconds(5),
        };
        tune?.Invoke(options);
        var node = new Node(new Endpoint("127.0.0.1", server.Port, server.Password), options);
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new Stack
        {
            Extension = extension,
            Node = node,
            Server = server,
            Logger = logger,
        };
    }

    [Test]
    public async Task Mix()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);
            var mix = $"/v4/sessions/{Session}/players/{Guild}/mix";

            server.On("GET", mix, 200, "null");
            var empty = await node
                .Rest.Mix.ListAsync(Guild, CancellationToken.None)
                .ConfigureAwait(false);
            await Assert.That(empty.Count).IsEqualTo(0);

            server.On("GET", mix, 200, """{"mixes":[null]}""");
            await AwaitRestAsync(() => node.Rest.Mix.ListAsync(Guild, CancellationToken.None))
                .ConfigureAwait(false);

            server.On(
                "GET",
                mix,
                200,
                """
                {"mixes":[{"id":"layer-1","track":{"encoded":"QAA="},"volume":0.5}]}
                """
            );
            var mixes = await node
                .Rest.Mix.ListAsync(Guild, CancellationToken.None)
                .ConfigureAwait(false);
            await Assert.That(mixes.Count).IsEqualTo(1);
            await Assert.That(mixes[0].Id).IsEqualTo("layer-1");
        }
    }

    [Test]
    public async Task RetryAfter()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);

            server.On("GET", "/v4/stats", call => new FakeRestResponse(429, "{}", RetryAfter: "2"));
            var delta = await Catch429Async(() =>
                    node.Rest.Info.GetStatsAsync(CancellationToken.None)
                )
                .ConfigureAwait(false);
            await Assert.That(delta?.StatusCode).IsEqualTo(429);
            await Assert.That(delta!.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(2));

            var future = DateTimeOffset
                .UtcNow.AddSeconds(30)
                .ToString("R", CultureInfo.InvariantCulture);
            server.On(
                "GET",
                "/v4/stats",
                call => new FakeRestResponse(429, "{}", RetryAfter: future)
            );
            var dated = await Catch429Async(() =>
                    node.Rest.Info.GetStatsAsync(CancellationToken.None)
                )
                .ConfigureAwait(false);
            await Assert
                .That(dated?.RetryAfter is { } span && span > TimeSpan.FromSeconds(10))
                .IsTrue();

            var past = DateTimeOffset
                .UtcNow.AddSeconds(-30)
                .ToString("R", CultureInfo.InvariantCulture);
            server.On(
                "GET",
                "/v4/stats",
                call => new FakeRestResponse(429, "{}", RetryAfter: past)
            );
            var stale = await Catch429Async(() =>
                    node.Rest.Info.GetStatsAsync(CancellationToken.None)
                )
                .ConfigureAwait(false);
            await Assert.That(stale?.RetryAfter).IsNull();
        }
    }

    private static async Task<RestException?> Catch429Async(Func<Task> call)
    {
        try
        {
            await call().ConfigureAwait(false);
            return null;
        }
        catch (RestException exception)
        {
            return exception;
        }
    }

    [Test]
    public async Task TruncatedErrorBody()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);

            var huge = new string('x', 70 * 1024);
            server.On("GET", "/v4/stats", 500, huge);
            var whole = await Catch429Async(() =>
                    node.Rest.Info.GetStatsAsync(CancellationToken.None)
                )
                .ConfigureAwait(false);
            await Assert.That(whole?.ResponseBody?.Length).IsEqualTo(huge.Length);
        }
    }

    [Test]
    public async Task InfoParser()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;

            var info = "/v4/info";

            stack.Server.On("GET", info, 200, "[]");
            var rooted = await CatchIncompatibleAsync(() =>
                    node.Rest.Info.GetServerInfoAsync(CancellationToken.None)
                )
                .ConfigureAwait(false);

            stack.Server.On(
                "GET",
                info,
                200,
                """{"version":{"semver":"3.9.1"}}{"version":{"semver":"1.0.0"}}"""
            );
            var doubled = await CatchIncompatibleAsync(() =>
                    node.Rest.Info.GetServerInfoAsync(CancellationToken.None)
                )
                .ConfigureAwait(false);

            stack.Server.On("GET", info, 200, """{"version":{"semver":42}}""");
            var numeric = await node
                .Rest.Info.GetServerInfoAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await Assert.That(numeric.Version).IsNull();
            await Assert.That(numeric.RawVersion.Length).IsEqualTo(0);

            stack.Server.On(
                "GET",
                info,
                200,
                """{"isNodelink":"yes","version":{"semver":"3.9.1"}}"""
            );
            var lied = await node
                .Rest.Info.GetServerInfoAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await Assert.That(lied.IsNodeLink).IsFalse();
            await Assert.That(lied.Version).IsNotNull();

            stack.Server.On(
                "GET",
                info,
                200,
                """{"filters":[42,"lowpass","highpass"],"version":{"semver":"3.9.1"}}"""
            );
            var filtered = await node
                .Rest.Info.GetServerInfoAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await Assert
                .That(filtered.EnabledFilters is { } set && set.SetEquals(["lowpass", "highpass"]))
                .IsTrue();

            stack.Server.On("GET", info, 200, """{"plugins":[42,{"name":"lava-lyrics"}]}""");
            var plugged = await node
                .Rest.Info.GetServerInfoAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await Assert.That(plugged.Plugins.SetEquals(["lava-lyrics"])).IsTrue();

            stack.Server.On("GET", info, 200, """{"version":{"semver":"3abc123def"}}""");
            var hashed = await node
                .Rest.Info.GetServerInfoAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await Assert.That(hashed.Version).IsNull();
            await Assert.That(hashed.RawVersion).IsEqualTo("3abc123def");
        }
    }

    private static async Task<IncompatibleServerException?> CatchIncompatibleAsync(Func<Task> call)
    {
        try
        {
            await call().ConfigureAwait(false);
            return null;
        }
        catch (IncompatibleServerException exception)
        {
            return exception;
        }
    }

    [Test]
    public async Task ProbeTimeout()
    {
        var server = new FakeNodeServer { GhostProbes = true };
        var logger = new CaptureLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "probe-ghost",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromMilliseconds(700),
                MaxReconnectAttempts = 0,
            }
        );
        extension.AddNode(node);

        var final = new TaskCompletionSource<Exception?>();
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                final.TrySetResult(args.Exception);
            return Task.CompletedTask;
        };

        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert
            .That(() => final.Task.IsCompleted)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await Assert.That(node.IsReady).IsFalse();
        await Assert.That(node.IsConnected).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task HandshakeTimeout()
    {
        var server = new FakeNodeServer { GhostHandshakes = true };
        var logger = new CaptureLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "handshake-ghost",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromMilliseconds(700),
                MaxReconnectAttempts = 0,
            }
        );
        extension.AddNode(node);

        var final = new TaskCompletionSource<Exception?>();
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                final.TrySetResult(args.Exception);
            return Task.CompletedTask;
        };

        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert
            .That(() => final.Task.IsCompleted)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await Assert.That(node.IsReady).IsFalse();
        await Assert.That(node.IsConnected).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task GiveUpInitialWithLabels()
    {
        var dead = new FakeNodeServer();
        var port = dead.Port;
        dead.Dispose();
        var logger = new CaptureLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", port, "pass"),
            new NodeOptions
            {
                Name = "labeled",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(100),
                MaxReconnectAttempts = 2,
            }
        );
        extension.AddNode(node);

        var final = new TaskCompletionSource<Exception?>();
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                final.TrySetResult(args.Exception);
            return Task.CompletedTask;
        };

        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert
            .That(() => final.Task.IsCompleted)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(20));
        using (Assert.Multiple()) { }

        await extension.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task GiveUpOnKilledConnection()
    {
        var server = new FakeNodeServer();
        var logger = new CaptureLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "killed",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                MaxReconnectAttempts = 0,
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await Assert.That(node.IsReady).IsTrue();

        var final = new TaskCompletionSource<Exception?>();
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                final.TrySetResult(args.Exception);
            return Task.CompletedTask;
        };

        server.KillConnection();
        await Assert
            .That(() => final.Task.IsCompleted)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await Assert.That(await final.Task.ConfigureAwait(false)).IsNotNull();
        await Assert.That(node.IsReady).IsFalse();
        await Assert.That(node.IsConnected).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    public async Task NeverThenConnected()
    {
        var server = new FakeNodeServer();
        var logger = new CaptureLogger();
        var bridge = new FakeBridge { Logger = logger };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "late-bloomer",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
            }
        );
        extension.AddNode(node);

        server.InfoStatus = 500;
        var final = new TaskCompletionSource<Exception?>();
        node.Disconnected += (_, args) =>
        {
            if (args.Final)
                final.TrySetResult(args.Exception);
            return Task.CompletedTask;
        };

        var connected = 0;
        var connectedSeen = new TaskCompletionSource<bool>();
        node.Connected += (_, _) =>
        {
            Interlocked.Increment(ref connected);
            if (connected >= 1)
                connectedSeen.TrySetResult(true);
            return Task.CompletedTask;
        };

        var connect = Task.Run(async () =>
        {
            try
            {
                await extension.ConnectAllNodesAsync().ConfigureAwait(false);
            }
            catch { }
        });
        await Task.Delay(500).ConfigureAwait(false);
        server.InfoStatus = 200;
        await Assert
            .That(() => node.IsReady)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(15));
        await Assert.That(final.Task.IsCompleted).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
        await connect.ConfigureAwait(false);
    }

    [Test]
    public async Task DropWithLogger()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);
            await Assert.That(node.IsReady).IsTrue();

            var reconnected = new TaskCompletionSource<bool>();
            node.Connected += (_, _) =>
            {
                reconnected.TrySetResult(true);
                return Task.CompletedTask;
            };

            await server.DropConnectionAsync(1000, "logged drop").ConfigureAwait(false);
            await Assert
                .That(
                    await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false)
                )
                .IsTrue();
        }
    }
}
