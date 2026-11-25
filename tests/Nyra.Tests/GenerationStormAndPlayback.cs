using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Nyra.Entities;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class GenerationStormAndPlayback
{
    private static readonly JsonSerializerOptions Options = NyraJson.SerializerOptions;

    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private const string StreamTrackJson =
        """{"encoded":"c3RyZWFt","info":{"identifier":"livestream","author":"Radio","title":"Live","length":0,"isStream":true,"isSeekable":false,"position":0}}""";

    private const string OpenTrackJson =
        """{"encoded":"emVybw==","info":{"identifier":"zerolength","author":"Odd","title":"Zero","length":0,"isStream":false,"isSeekable":true,"position":0}}""";

    private static Track ParseTrack() => JsonSerializer.Deserialize<Track>(TrackJson, Options)!;

    private static Track ParseStreamTrack() =>
        JsonSerializer.Deserialize<Track>(StreamTrackJson, Options)!;

    private static string PlayerJson(
        string? trackJson,
        bool paused = false,
        int volume = 100,
        string? stateJson = null
    ) => $$"""
        {
            "guildId": "777",
            "track": {{trackJson ?? "null"}},
            "volume": {{volume}},
            "paused": {{(paused ? "true" : "false")}},
            "state": {{stateJson ?? "null"}},
            "voice": {"token": "tok", "endpoint": "127.0.0.1", "sessionId": "vsess"}
        }
        """;

    private static string StateJson(long time, long position, bool connected) =>
        $$"""{"time": {{time}}, "position": {{position}}, "connected": {{(connected ? "true" : "false")}}, "ping": 42}""";

    private sealed class Rig : IAsyncDisposable
    {
        public required FakeBridge Bridge { get; init; }
        public required NyraExtension Extension { get; init; }
        public required Node Node { get; init; }
        public required FakeNodeServer Server { get; init; }
        public required GuildConnection Connection { get; init; }
        public required List<string> Reply { get; init; }
        public required List<TaskCompletionSource?> Gate { get; init; }
        public string PlayerReply
        {
            get => Reply[0];
            set => Reply[0] = value;
        }

        public TaskCompletionSource? GetGate
        {
            get => Gate[0];
            set => Gate[0] = value;
        }

        public int RestCalls => Server.RestCalls.Count;

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private static async Task<Rig> RigAsync(
        bool lavalink = false,
        bool multiGuild = false,
        TimeSpan? handshakeTimeout = null,
        TimeSpan? readyTimeout = null,
        TimeProvider? time = null
    )
    {
        var server = new FakeNodeServer
        {
            IsNodeLink = !lavalink,
            Semver = lavalink ? "4.2.0" : "3.9.1",
        };
        var holder = new List<string> { PlayerJson(null) };
        var gate = new List<TaskCompletionSource?> { null };
        server.On("PATCH", "/v4/sessions/fake-session/players/777", _ => new FakeRestResponse(
            200,
            holder[0]
        ));
        server.OnRaw("GET", "/v4/sessions/fake-session/players/777", async (_, stream) =>
        {
            var pending = gate[0];
            if (pending is not null)
                await pending.Task.ConfigureAwait(false);
            var body = System.Text.Encoding.UTF8.GetBytes(holder[0]);
            var head =
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(head)).ConfigureAwait(false);
            await stream.WriteAsync(body).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
            stream.Close();
        });
        if (multiGuild)
            for (var guild = 0; guild < 12; guild++)
            {
                var guildId = 900_000UL + (ulong)guild;
                server.On(
                    "PATCH",
                    $"/v4/sessions/fake-session/players/{guildId}",
                    _ => new FakeRestResponse(200, PlayerJson(null))
                );
                server.On(
                    "GET",
                    $"/v4/sessions/fake-session/players/{guildId}",
                    _ => new FakeRestResponse(200, PlayerJson(null))
                );
            }
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge, time);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "storm",
                TimeProvider = time ?? TimeProvider.System,
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(5),
                ReadyTimeout = readyTimeout ?? TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var connect = node.ConnectAsync(777, 555);
        await WaitForJoinAsync(bridge).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        var connection = await connect.ConfigureAwait(false);
        return new Rig
        {
            Bridge = bridge,
            Extension = extension,
            Node = node,
            Server = server,
            Connection = connection,
            Reply = holder,
            Gate = gate,
        };
    }

    private static void SetReply(Rig rig, string reply) => rig.PlayerReply = reply;

    private static async Task PushAsync(Rig rig, string json)
    {
        await rig.Server.PushAsync(json).ConfigureAwait(false);
    }

    private static Task PushUpdateAsync(
        Rig rig,
        long position,
        bool connected,
        long time,
        bool withState = true
    ) =>
        PushAsync(
            rig,
            withState
                ? $$"""{"op":"playerUpdate","guildId":"777","state":{{StateJson(time, position, connected)}}}"""
                : """{"op":"playerUpdate","guildId":"777"}"""
        );

    private static Task PushEndAsync(Rig rig, string? trackBody, string reason) =>
        PushAsync(
            rig,
            $$"""{"op":"event","guildId":"777","type":"TrackEndEvent","track":{{trackBody ?? "null"}},"reason":"{{reason}}"}"""
        );

    private static Task PushPauseAsync(Rig rig, bool paused) =>
        PushAsync(
            rig,
            $$"""{"op":"event","guildId":"777","type":"PauseEvent","paused":{{(paused ? "true" : "false")}}}"""
        );

    private static Task PushVolumeAsync(Rig rig, int volume) =>
        PushAsync(
            rig,
            $$"""{"op":"event","guildId":"777","type":"VolumeChangedEvent","volume":{{volume}}}"""
        );

    private static async Task PlayAsync(Rig rig, Track track)
    {
        await Clock.NextTickAsync().ConfigureAwait(false);
        SetReply(rig, PlayerJson(TrackBodyFor(track)));
        await rig.Connection.PlayAsync(track).ConfigureAwait(false);
        await Assert
            .That(() => rig.Connection.CurrentTrack?.Encoded)
            .WaitsFor(assert => assert.IsEqualTo(track.Encoded), TimeSpan.FromSeconds(5));
    }

    private static string TrackBodyFor(Track track) =>
        track.Info.IsStream ? StreamTrackJson
        : track.Encoded == "emVybw==" ? OpenTrackJson
        : TrackJson;

    [Test]
    public async Task HandshakeStorm()
    {
        var time = new FakeTimeProvider();
        var rig = await RigAsync(handshakeTimeout: TimeSpan.FromMilliseconds(250), time: time)
            .ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var raced = 0;
            var failed = 0;
            for (var round = 0; round < 10; round++)
            {
                var connects = new Task[6];
                for (var attempt = 0; attempt < connects.Length; attempt++)
                    connects[attempt] = rig.Node.ConnectAsync(888_000UL, 555);
                var all = Task.WhenAll(connects);
                await AdvanceUntilAsync(
                        time,
                        all,
                        TimeSpan.FromMilliseconds(100),
                        TimeSpan.FromSeconds(10)
                    )
                    .ConfigureAwait(false);
                try
                {
                    await all.ConfigureAwait(false);
                }
                catch
                {
                    failed++;
                }
                if (connects.Any(c => c.IsFaulted))
                    raced++;
            }
            await Assert.That(failed).IsEqualTo(10);
            await Assert.That(raced).IsEqualTo(10);
            await RunForAsync(time, TimeSpan.FromMilliseconds(600)).ConfigureAwait(false);
            await Assert
                .That(() => rig.Node.GetConnection(888_000UL) is null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task PlaybackCapture()
    {
        var rig = await RigAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var track = ParseTrack();
            var stream = ParseStreamTrack();
            await PlayAsync(rig, track).ConfigureAwait(false);
            await PushUpdateAsync(rig, 777, false, 100).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds == 777)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            SetReply(rig, PlayerJson(TrackJson, stateJson: StateJson(150, 12345, true)));
            var callsAtStart = rig.RestCalls;
            rig.GetGate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var endRefresh = rig.Connection.RefreshAsync();
            await Assert
                .That(() => rig.RestCalls > callsAtStart)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await PushEndAsync(rig, TrackJson, "finished").ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.CurrentTrack is null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            rig.GetGate?.TrySetResult();
            await endRefresh.ConfigureAwait(false);
            await Assert.That(rig.Connection.CurrentTrack).IsNull();
            await Assert.That(rig.Connection.Position?.TotalMilliseconds).IsEqualTo(777);

            SetReply(rig, PlayerJson(null, paused: true));
            await rig.Connection.PauseAsync().ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.IsPaused)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Clock.NextTickAsync().ConfigureAwait(false);

            SetReply(rig, PlayerJson(null, paused: false, stateJson: StateJson(260, 0, true)));
            var callsAtPause = rig.RestCalls;
            rig.GetGate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var pauseRefresh = rig.Connection.RefreshAsync();
            await Assert
                .That(() => rig.RestCalls > callsAtPause)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            var pauseEchoed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            rig.Connection.PauseChanged += (_, _) =>
            {
                pauseEchoed.TrySetResult();
                return Task.CompletedTask;
            };
            await PushPauseAsync(rig, true).ConfigureAwait(false);
            await pauseEchoed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await Assert.That(rig.Connection.IsPaused).IsTrue();
            rig.GetGate?.TrySetResult();
            await pauseRefresh.ConfigureAwait(false);
            await Assert.That(rig.Connection.IsPaused).IsTrue();

            SetReply(rig, PlayerJson(null, volume: 99, stateJson: StateJson(270, 0, true)));
            var callsAtVolume = rig.RestCalls;
            rig.GetGate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var volumeRefresh = rig.Connection.RefreshAsync();
            await Assert
                .That(() => rig.RestCalls > callsAtVolume)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await PushVolumeAsync(rig, 80).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Volume == 80)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            rig.GetGate?.TrySetResult();
            await volumeRefresh.ConfigureAwait(false);
            await Assert.That(rig.Connection.Volume).IsEqualTo(80);

            SetReply(rig, PlayerJson(StreamTrackJson));
            await rig.Connection.RefreshAsync().ConfigureAwait(false);
            await Assert.That(rig.Connection.CurrentTrack?.Encoded).IsEqualTo(stream.Encoded);
            await Assert.That(rig.Connection.Position?.TotalMilliseconds).IsEqualTo(777);

            SetReply(rig, PlayerJson(null, stateJson: StateJson(90, 9999, true)));
            await rig.Connection.RefreshAsync().ConfigureAwait(false);
            await Assert.That(rig.Connection.Position?.TotalMilliseconds).IsEqualTo(777);

            SetReply(rig, PlayerJson(TrackJson, stateJson: StateJson(400, 12345, false)));
            await rig.Connection.RefreshAsync().ConfigureAwait(false);
            await Assert.That(rig.Connection.Position?.TotalMilliseconds).IsEqualTo(12345);
        }
    }

    private sealed class ThrowingLogger : ILogger
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
        ) => throw new InvalidOperationException("generation storm: the logger itself dies");
    }
}
