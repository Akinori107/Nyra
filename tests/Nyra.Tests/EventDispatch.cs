using Microsoft.Extensions.Logging;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class EventDispatch
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private sealed class DebugLogger(List<string> sink) : ILogger
    {
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            lock (sink)
                sink.Add($"{logLevel}: {formatter(state, exception)}");
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }

    private static bool SinkHas(List<string> sink, string fragment)
    {
        lock (sink)
            return sink.Any(l => l.Contains(fragment));
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
                Name = "events",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
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

    [Test]
    public async Task MissingGuildEvents()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            var lyricsFound = new TaskCompletionSource<string?>();
            conn.LyricsFound += (_, args) =>
            {
                lyricsFound.TrySetResult(args.Lyrics.SourceName);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"LyricsFoundEvent","lyrics":{"sourceName":"lrclib","text":"first line"}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    await lyricsFound.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                )
                .IsEqualTo("lrclib");

            var volumeChanged = new TaskCompletionSource<int>();
            conn.VolumeChanged += (_, args) =>
            {
                volumeChanged.TrySetResult(args.Volume);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"VolumeChangedEvent","volume":80}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    await volumeChanged
                        .Task.WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false)
                )
                .IsEqualTo(80);

            var filtersChanged = new TaskCompletionSource<float?>();
            conn.FiltersChanged += (_, args) =>
            {
                filtersChanged.TrySetResult(args.Filters?.Tremolo?.Frequency);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"FiltersChangedEvent","filters":{"tremolo":{"frequency":8,"depth":0.5}}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    await filtersChanged
                        .Task.WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false)
                )
                .IsEqualTo(8f);

            var sought = new TaskCompletionSource<long>();
            conn.TrackSought += (_, args) =>
            {
                sought.TrySetResult(args.PositionMilliseconds);
                return Task.CompletedTask;
            };
            await server
                .PushAsync("""{"op":"event","guildId":"777","type":"SeekEvent","position":30000}""")
                .ConfigureAwait(false);
            await Assert
                .That(await sought.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsEqualTo(30000);

            var pauseChanged = new TaskCompletionSource<bool>();
            conn.PauseChanged += (_, args) =>
            {
                pauseChanged.TrySetResult(args.IsPaused);
                return Task.CompletedTask;
            };
            await server
                .PushAsync("""{"op":"event","guildId":"777","type":"PauseEvent","paused":true}""")
                .ConfigureAwait(false);
            await Assert
                .That(
                    await pauseChanged.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                )
                .IsTrue();

            var connected = new TaskCompletionSource<string?>();
            conn.PlayerConnected += (_, args) =>
            {
                connected.TrySetResult(args.Voice?.Endpoint);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"PlayerConnectedEvent","voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await connected.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsEqualTo("127.0.0.1");

            var eternalBox = new TaskCompletionSource<string?>();
            conn.EternalBoxInfoUpdated += (_, args) =>
            {
                eternalBox.TrySetResult(args.Info.Id);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    "{\"op\":\"event\",\"guildId\":\"777\",\"type\":\"EternalBoxInfoEvent\",\"track\":"
                        + TrackJson
                        + ",\"eternalbox\":{\"id\":\"box-1\",\"url\":\"https://box\"}}"
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    await eternalBox.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                )
                .IsEqualTo("box-1");
        }
    }

    [Test]
    public async Task UnknownFrames()
    {
        var sink = new List<string>();
        var server = new FakeNodeServer();
        var bridge = new FakeBridge { Logger = new DebugLogger(sink) };
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "unknown",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

        await server.PushAsync("""{"op":"mystery","guildId":"777"}""").ConfigureAwait(false);
        await Assert
            .That(() => SinkHas(sink, "unknown operation 'mystery'"))
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

        await server
            .PushAsync("""{"op":"event","guildId":"777","type":"MysteryEvent"}""")
            .ConfigureAwait(false);
        await Assert
            .That(() => SinkHas(sink, "unknown event type 'MysteryEvent'"))
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

        await server.PushAsync("""{"guildId":"777"}""").ConfigureAwait(false);

        await server.PushAsync("""{"op":"event","guildId":"777"}""").ConfigureAwait(false);

        await server.PushAsync("""{"op":123,"guildId":"777"}""").ConfigureAwait(false);

        await server
            .PushAsync("""{"op":"event","guildId":"777","type":123}""")
            .ConfigureAwait(false);

        await server
            .PushAsync("""{"op":"event","guildId":"999","type":"VolumeChangedEvent","volume":1}""")
            .ConfigureAwait(false);

        var probe = new TaskCompletionSource<int>();
        conn.VolumeChanged += (_, args) =>
        {
            probe.TrySetResult(args.Volume);
            return Task.CompletedTask;
        };
        await server
            .PushAsync("""{"op":"event","guildId":"777","type":"VolumeChangedEvent","volume":42}""")
            .ConfigureAwait(false);
        await Assert
            .That(await probe.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            .IsEqualTo(42);

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    private const string StatsJson =
        """{"op":"stats","players":1,"playingPlayers":1,"uptime":500,"memory":{"free":1,"used":2,"allocated":3,"reservable":4},"cpu":{"cores":2,"systemLoad":0.1,"lavalinkLoad":0.2}}""";

    [Test]
    public async Task FragmentedFrames()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);

            var stats = 0;
            node.StatsUpdated += (_, _) =>
            {
                Interlocked.Increment(ref stats);
                return Task.CompletedTask;
            };
            await server
                .PushFrameAsync(0x1, false, """{"op":"stats","play""")
                .ConfigureAwait(false);
            await server
                .PushFrameAsync(0x0, false, """ers":1,"playingPlay""")
                .ConfigureAwait(false);
            await server
                .PushFrameAsync(
                    0x0,
                    true,
                    """ers":1,"uptime":500,"memory":{"free":1,"used":2,"allocated":3,"reservable":4},"cpu":{"cores":2,"systemLoad":0.1,"lavalinkLoad":0.2}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(() => Volatile.Read(ref stats) == 1)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await server.PushFrameAsync(0x2, false, "binary-part-").ConfigureAwait(false);
            await server.PushFrameAsync(0x0, true, "binary-rest").ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
            await Assert.That(Volatile.Read(ref stats)).IsEqualTo(1);

            var connections = server.WebsocketConnections;
            await server
                .PushFrameAsync(0x1, false, """{"op":"stats","players""")
                .ConfigureAwait(false);
            await server.DropConnectionAsync(1000, "mid-fragment").ConfigureAwait(false);
            await Assert
                .That(() => Volatile.Read(ref server.WebsocketConnections) > connections)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(10));
            await Task.Delay(150).ConfigureAwait(false);
            await server.PushAsync(StatsJson).ConfigureAwait(false);
            await Assert
                .That(() => Volatile.Read(ref stats) == 2)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            connections = server.WebsocketConnections;
            var oversized = new string('x', 36_000);
            for (var i = 0; i < 30; i++)
                await server
                    .PushFrameAsync(i == 0 ? 0x1 : 0x0, i == 29, oversized)
                    .ConfigureAwait(false);
            await Assert
                .That(() => Volatile.Read(ref server.WebsocketConnections) > connections)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(10));
            await Task.Delay(150).ConfigureAwait(false);
            await server.PushAsync(StatsJson).ConfigureAwait(false);
            await Assert
                .That(() => Volatile.Read(ref stats) == 3)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await stack.Extension.DisposeAsync().ConfigureAwait(false);
            server.Dispose();
        }
    }
}
