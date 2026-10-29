using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nyra.Abstractions;
using Nyra.Entities;
using Nyra.Entities.Filters;

namespace Nyra.Tests;

public sealed class GenerationStormAndPlayback
{
    private static readonly JsonSerializerOptions Options = NyraJson.SerializerOptions;

    private const string Encoded = "QXRsZWFzdA==";

    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private const string StreamTrackJson =
        """{"encoded":"c3RyZWFt","info":{"identifier":"livestream","author":"Radio","title":"Live","length":0,"isStream":true,"isSeekable":false,"position":0}}""";

    private const string OpenTrackJson =
        """{"encoded":"emVybw==","info":{"identifier":"zerolength","author":"Odd","title":"Zero","length":0,"isStream":false,"isSeekable":true,"position":0}}""";

    private static Track ParseTrack() => JsonSerializer.Deserialize<Track>(TrackJson, Options)!;

    private static Track ParseStreamTrack() =>
        JsonSerializer.Deserialize<Track>(StreamTrackJson, Options)!;

    private static Track ParseOpenTrack() =>
        JsonSerializer.Deserialize<Track>(OpenTrackJson, Options)!;

    private static string PlayerJson(
        string? trackJson,
        bool paused = false,
        int volume = 100,
        string? stateJson = null
    ) =>
        $$"""
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
        TimeSpan? readyTimeout = null
    )
    {
        var server = new FakeNodeServer
        {
            IsNodeLink = !lavalink,
            Semver = lavalink ? "4.2.0" : "3.9.1",
        };
        var holder = new List<string> { PlayerJson(null) };
        var gate = new List<TaskCompletionSource?> { null };
        server.On(
            "PATCH",
            "/v4/sessions/fake-session/players/777",
            _ => new FakeRestResponse(200, holder[0])
        );
        server.OnRaw(
            "GET",
            "/v4/sessions/fake-session/players/777",
            async (_, stream) =>
            {
                var pending = gate[0];
                if (pending is not null)
                    await pending.Task.ConfigureAwait(false);
                var body = System.Text.Encoding.UTF8.GetBytes(holder[0]);
                var head =
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                await stream
                    .WriteAsync(System.Text.Encoding.UTF8.GetBytes(head))
                    .ConfigureAwait(false);
                await stream.WriteAsync(body).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Close();
            }
        );
        if (multiGuild)
        {
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
        }
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Nyra.Endpoint("127.0.0.1", server.Port, server.Password),
            new Nyra.NodeOptions
            {
                Name = "storm",
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
        await Task.Delay(100).ConfigureAwait(false);
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

    private static Task PushStartAsync(Rig rig, string? trackBody) =>
        PushAsync(
            rig,
            $$"""{"op":"event","guildId":"777","type":"TrackStartEvent","track":{{trackBody ?? "null"}}}"""
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

    private static Task PushSeekAsync(Rig rig, long position) =>
        PushAsync(
            rig,
            $$"""{"op":"event","guildId":"777","type":"SeekEvent","position":{{position}}}"""
        );

    private static async Task AwaitReconnectAsync(Rig rig)
    {
        await Assert
            .That(() => rig.Node.IsConnected)
            .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(10));
        await Task.Delay(250).ConfigureAwait(false);
    }

    private static async Task PlayAsync(Rig rig, Track track)
    {
        await Reflect.NextTickAsync().ConfigureAwait(false);
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
    public async Task AbandonedGeneration()
    {
        var rig = await RigAsync(readyTimeout: TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        try
        {
            rig.Server.SuppressReady = true;
            await rig
                .Server.DropConnectionAsync(4006, "generation storm: abandon")
                .ConfigureAwait(false);
            await Assert
                .That(() => Volatile.Read(ref rig.Server.WebsocketConnections) >= 2)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromMilliseconds(10_000));
            var hung = rig.Server.CaptureWebsocket();
            rig.Server.SuppressReady = false;
            await rig
                .Server.PushOnCapturedAsync(
                    hung!,
                    """{"op":"event","guildId":"777","type":"VolumeChangedEvent","volume":61}"""
                )
                .ConfigureAwait(false);

            await Assert
                .That(() => Volatile.Read(ref rig.Server.WebsocketConnections) >= 3)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromMilliseconds(15_000));

            try
            {
                await rig
                    .Server.PushOnCapturedAsync(
                        hung!,
                        """{"op":"ready","resumed":false,"sessionId":"stale-ready"}"""
                    )
                    .ConfigureAwait(false);
            }
            catch { }
            await Task.Delay(200).ConfigureAwait(false);
            await Assert.That(rig.Node.IsConnected).IsTrue();

            for (var frame = 0; frame < 120; frame++)
            {
                try
                {
                    await rig
                        .Server.PushOnCapturedAsync(
                            hung!,
                            $$"""{"op":"event","guildId":"777","type":"VolumeChangedEvent","volume":{{62 + frame}}}"""
                        )
                        .ConfigureAwait(false);
                }
                catch { }
                await Task.Delay(10).ConfigureAwait(false);
            }
            rig.Server.KillCaptured(hung!);
            await Task.Delay(300).ConfigureAwait(false);
            await Assert.That(rig.Node.IsConnected).IsTrue();
            await Assert.That(rig.Node.IsConnected).IsTrue();
        }
        finally
        {
            await rig.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task HandshakeStorm()
    {
        var rig = await RigAsync(handshakeTimeout: TimeSpan.FromMilliseconds(250))
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
                try
                {
                    await Task.WhenAll(connects).ConfigureAwait(false);
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
            await Task.Delay(600).ConfigureAwait(false);
            await Assert
                .That(() => rig.Node.GetConnection(888_000UL) is null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task PlaybackRead()
    {
        var rig = await RigAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var track = ParseTrack();
            await Assert.That(rig.Connection.Position).IsNull();
            await PushUpdateAsync(rig, 100, true, 1, withState: false).ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
            await Assert.That(rig.Connection.Position).IsNull();

            await PlayAsync(rig, track).ConfigureAwait(false);
            await PushUpdateAsync(rig, -50, false, 10).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position == TimeSpan.Zero)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await PushUpdateAsync(rig, 4321, false, 20).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds == 4321)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await PushUpdateAsync(rig, 5000, false, 2000).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds == 5000)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await PushUpdateAsync(rig, 9999, false, 1000).ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await Assert.That(rig.Connection.Position?.TotalMilliseconds).IsEqualTo(5000);

            await PushUpdateAsync(rig, 175_000, true, 3000).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds >= 175_000)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Task.Delay(1200).ConfigureAwait(false);
            await Assert.That(rig.Connection.Position?.TotalMilliseconds).IsEqualTo(176_000);

            SetReply(rig, PlayerJson(null));
            var stream = ParseStreamTrack();
            await PlayAsync(rig, stream).ConfigureAwait(false);
            await PushUpdateAsync(rig, 2500, true, 4000).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds == 2500)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        }

        var openRig = await RigAsync().ConfigureAwait(false);
        await using (openRig.ConfigureAwait(false))
        {
            var open = ParseOpenTrack();
            await PlayAsync(openRig, open).ConfigureAwait(false);
            await PushUpdateAsync(openRig, 1000, true, 100).ConfigureAwait(false);
            await Assert
                .That(() => openRig.Connection.Position?.TotalMilliseconds >= 1000)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Task.Delay(400).ConfigureAwait(false);
            var grown = openRig.Connection.Position?.TotalMilliseconds ?? 0;
            await Assert.That(grown is >= 1100 and <= 3000).IsTrue();
        }
    }

    [Test]
    public async Task PlaybackStartEnd()
    {
        var rig = await RigAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var track = ParseTrack();
            var stream = ParseStreamTrack();
            await PlayAsync(rig, track).ConfigureAwait(false);
            await PushUpdateAsync(rig, 7000, false, 100).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds == 7000)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await PushStartAsync(rig, TrackJson).ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await Assert.That(rig.Connection.Position?.TotalMilliseconds).IsEqualTo(7000);

            await PushUpdateAsync(rig, 8000, true, 200).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds >= 8000)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await PushEndAsync(rig, TrackJson, "finished").ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.CurrentTrack is null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            var frozen = rig.Connection.Position?.TotalMilliseconds ?? 0;
            await Assert.That(frozen).IsGreaterThanOrEqualTo(8000);
            await PushStartAsync(rig, TrackJson).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds is >= 0 and <= 500)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await PushUpdateAsync(rig, 8000, false, 300).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds == 8000)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await PushStartAsync(rig, TrackJson).ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await Assert.That(rig.Connection.Position?.TotalMilliseconds).IsEqualTo(8000);
            await Assert.That(rig.Connection.CurrentTrack).IsNotNull();

            SetReply(rig, PlayerJson(null));
            await PlayAsync(rig, stream).ConfigureAwait(false);
            await PushUpdateAsync(rig, 2500, true, 400).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds == 2500)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await PushEndAsync(rig, StreamTrackJson, "finished").ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.CurrentTrack is null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await PushUpdateAsync(rig, 1500, false, 500).ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            var beforeNullStart = rig.Connection.Position?.TotalMilliseconds ?? 0;
            await PushStartAsync(rig, null).ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await Assert
                .That(rig.Connection.Position?.TotalMilliseconds)
                .IsEqualTo(beforeNullStart);
            await Assert.That(rig.Connection.CurrentTrack).IsNull();

            await PushPauseAsync(rig, false).ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await Assert
                .That(rig.Connection.Position?.TotalMilliseconds)
                .IsEqualTo(beforeNullStart);
        }

        var endRig = await RigAsync().ConfigureAwait(false);
        await using (endRig.ConfigureAwait(false))
        {
            var track = ParseTrack();
            await PlayAsync(endRig, track).ConfigureAwait(false);
            await PushUpdateAsync(endRig, 6000, false, 100).ConfigureAwait(false);
            await Assert
                .That(() => endRig.Connection.Position?.TotalMilliseconds == 6000)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await PushEndAsync(endRig, null, "finished").ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await Assert.That(endRig.Connection.CurrentTrack?.Encoded).IsEqualTo(Encoded);

            await PushEndAsync(endRig, TrackJson, "finished").ConfigureAwait(false);
            await Assert
                .That(() => endRig.Connection.CurrentTrack is null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await PushEndAsync(endRig, null, "finished").ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await Assert.That(endRig.Connection.CurrentTrack).IsNull();
            await Assert.That(endRig.Connection.Position?.TotalMilliseconds).IsEqualTo(6000);

            await PlayAsync(endRig, track).ConfigureAwait(false);
            await PushUpdateAsync(endRig, 9000, false, 200).ConfigureAwait(false);
            await Assert
                .That(() => endRig.Connection.Position?.TotalMilliseconds == 9000)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await PushEndAsync(endRig, TrackJson, "replaced").ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await Assert.That(endRig.Connection.CurrentTrack?.Encoded).IsEqualTo(Encoded);
            await PushEndAsync(endRig, null, "finished").ConfigureAwait(false);
            await Assert
                .That(() => endRig.Connection.CurrentTrack is null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await PushSeekAsync(endRig, 999).ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await Assert.That(endRig.Connection.Position?.TotalMilliseconds).IsEqualTo(9000);

            await PlayAsync(endRig, track).ConfigureAwait(false);
            await PushSeekAsync(endRig, 30_000).ConfigureAwait(false);
            await Assert
                .That(() =>
                    endRig.Connection.Position?.TotalMilliseconds is >= 30_000 and <= 31_000
                )
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await PushVolumeAsync(endRig, 80).ConfigureAwait(false);
            await Assert
                .That(() => endRig.Connection.Volume == 80)
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
            await Reflect.NextTickAsync().ConfigureAwait(false);

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

    [Test]
    public async Task PlaybackFold()
    {
        var rig = await RigAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var track = ParseTrack();
            await PlayAsync(rig, track).ConfigureAwait(false);
            await PushUpdateAsync(rig, 175_000, true, 100).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds >= 175_000)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            SetReply(rig, PlayerJson(TrackJson, stateJson: StateJson(150, 175_000, true)));
            await rig
                .Connection.Filters.AddAsync(new TimescaleFilter(Speed: 2.0))
                .ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds == 176_000)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await PushUpdateAsync(rig, 175_000, true, 200).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds is >= 175_000 and < 175_500)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            SetReply(rig, PlayerJson(TrackJson, stateJson: StateJson(250, 175_000, true)));
            await rig
                .Connection.Filters.AddAsync(new TimescaleFilter(Speed: 1.5))
                .ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds is >= 175_000 and <= 176_200)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await PushPauseAsync(rig, true).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.IsPaused)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            var frozenAt = rig.Connection.Position?.TotalMilliseconds ?? 0;
            SetReply(
                rig,
                PlayerJson(TrackJson, paused: true, stateJson: StateJson(300, 175_000, false))
            );
            await rig
                .Connection.Filters.AddAsync(new TimescaleFilter(Speed: 1.25))
                .ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await Assert.That(rig.Connection.Position?.TotalMilliseconds).IsEqualTo(frozenAt);
        }

        var pauseRig = await RigAsync().ConfigureAwait(false);
        await using (pauseRig.ConfigureAwait(false))
        {
            var track = ParseTrack();
            await PlayAsync(pauseRig, track).ConfigureAwait(false);
            await PushUpdateAsync(pauseRig, 175_000, true, 100).ConfigureAwait(false);
            await Assert
                .That(() => pauseRig.Connection.Position?.TotalMilliseconds >= 175_000)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Task.Delay(1100).ConfigureAwait(false);
            await PushPauseAsync(pauseRig, true).ConfigureAwait(false);
            await Assert
                .That(() => pauseRig.Connection.Position?.TotalMilliseconds == 176_000)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await PushPauseAsync(pauseRig, false).ConfigureAwait(false);
            await Assert
                .That(() => !pauseRig.Connection.IsPaused)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            var resumedAt = pauseRig.Connection.Position?.TotalMilliseconds ?? 0;
            await Assert.That(resumedAt is >= 176_000 and <= 176_600).IsTrue();

            await PushPauseAsync(pauseRig, true).ConfigureAwait(false);
            await Assert
                .That(() => pauseRig.Connection.IsPaused)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Task.Delay(300).ConfigureAwait(false);
            await Assert
                .That(pauseRig.Connection.Position?.TotalMilliseconds is >= 176_000 and <= 176_900)
                .IsTrue();
        }

        var openRig = await RigAsync().ConfigureAwait(false);
        await using (openRig.ConfigureAwait(false))
        {
            var open = ParseOpenTrack();
            await PlayAsync(openRig, open).ConfigureAwait(false);
            await PushUpdateAsync(openRig, 2100, true, 100).ConfigureAwait(false);
            await Assert
                .That(() => openRig.Connection.Position?.TotalMilliseconds >= 2100)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await PushPauseAsync(openRig, true).ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await Assert
                .That(openRig.Connection.Position?.TotalMilliseconds is >= 2100 and <= 2600)
                .IsTrue();
        }
    }

    [Test]
    public async Task Forwarder()
    {
        var rig = await RigAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var seenSenders = new List<object?>();
            rig.Extension.NodeConnected += (sender, _) =>
            {
                seenSenders.Add(sender);
                return Task.CompletedTask;
            };
            rig.Extension.NodeStatsUpdated += (sender, _) =>
            {
                seenSenders.Add(sender);
                return Task.CompletedTask;
            };
            rig.Extension.NodeWorkerFailed += (sender, _) =>
            {
                seenSenders.Add(sender);
                return Task.CompletedTask;
            };
            await rig
                .Server.PushAsync(
                    """{"op":"stats","players":1,"playingPlayers":1,"uptime":5,"memory":{"free":1,"used":1,"allocated":1,"reservable":1},"cpu":{"cores":2,"systemLoad":0.1,"lavalinkLoad":0.1}}"""
                )
                .ConfigureAwait(false);
            await rig
                .Server.PushAsync(
                    """{"op":"event","type":"WorkerFailedEvent","workerId":3,"affectedGuilds":["777"]}"""
                )
                .ConfigureAwait(false);
            await rig
                .Server.DropConnectionAsync(4006, "generation storm: fwd")
                .ConfigureAwait(false);
            await Assert
                .That(() => seenSenders.Count >= 3)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Assert.That(seenSenders.All(s => ReferenceEquals(s, rig.Node))).IsTrue();

            var disconnects = 0L;
            rig.Extension.NodeDisconnected += (_, _) =>
            {
                Interlocked.Increment(ref disconnects);
                return Task.CompletedTask;
            };
            var seen = 0L;
            var sink = new List<string>();
            rig.Bridge.Logger = new ListLogger(sink);
            rig.Extension.NodeDisconnected += (_, _) =>
            {
                Interlocked.Increment(ref seen);
                throw new InvalidOperationException("generation storm: f2");
            };
            await rig
                .Server.DropConnectionAsync(4006, "generation storm: fwd2")
                .ConfigureAwait(false);
            await Assert
                .That(() => Interlocked.Read(ref seen) == 1)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Assert.That(sink.Count).IsGreaterThanOrEqualTo(1);
            await Assert
                .That(sink)
                .Contains(m => m.Contains("generation storm: f2") && m.Contains("EventArgs"));

            await AwaitReconnectAsync(rig).ConfigureAwait(false);
            var before = Interlocked.Read(ref disconnects);
            rig.Bridge.Logger = new ThrowingLogger();
            rig.Extension.NodeDisconnected += (_, _) =>
                throw new InvalidOperationException("generation storm: f3");
            await rig
                .Server.DropConnectionAsync(4006, "generation storm: fwd3")
                .ConfigureAwait(false);
            await Assert
                .That(() => Interlocked.Read(ref disconnects) > before)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await AwaitReconnectAsync(rig).ConfigureAwait(false);
            var beforeQuiet = Interlocked.Read(ref disconnects);
            rig.Bridge.Logger = null;
            rig.Extension.NodeDisconnected += (_, _) =>
                throw new InvalidOperationException("generation storm: f1");
            await rig
                .Server.DropConnectionAsync(4006, "generation storm: fwd4")
                .ConfigureAwait(false);
            await Assert
                .That(() => Interlocked.Read(ref disconnects) > beforeQuiet)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        }
    }

    private sealed class AsyncThrowerBridge : IDiscordBridge, IAsyncDisposable
    {
        public Microsoft.Extensions.Logging.ILogger? Logger { get; set; }

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

        public Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task<ClientInformation> WaitForReadyAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ClientInformation(1UL));

        public ValueTask DisposeAsync() =>
            throw new InvalidOperationException(
                "generation storm: the async bridge dies on dispose"
            );
    }

    private sealed class PlainBridge : IDiscordBridge
    {
        public Microsoft.Extensions.Logging.ILogger? Logger => null;

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

        public Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task<ClientInformation> WaitForReadyAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ClientInformation(1UL));
    }

    private sealed class ListLogger(List<string> sink) : ILogger
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
            var text = formatter(state, exception);
            if (exception is not null)
                text += " " + exception.GetType().Name + " " + exception.Message;
            sink.Add(text);
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

    private sealed class LoudBridge : IDiscordBridge, IDisposable
    {
        public Microsoft.Extensions.Logging.ILogger? Logger { get; set; }

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

        public Task SendVoiceUpdateAsync(
            ulong guildId,
            ulong? channelId,
            bool selfDeaf = false,
            bool selfMute = false,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task<ClientInformation> WaitForReadyAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ClientInformation(1UL));

        public void Dispose() =>
            throw new InvalidOperationException(
                "generation storm: the loud bridge dies on dispose"
            );
    }

    [Test]
    public async Task DisposeFailure()
    {
        var loudSink = new List<string>();
        var loud = new NyraExtension(new LoudBridge { Logger = new ListLogger(loudSink) });
        await Assert
            .That(async () =>
            {
                await loud.DisposeAsync().ConfigureAwait(false);
            })
            .Throws<InvalidOperationException>()
            .WithMessageContaining("generation storm: the loud bridge dies");
        await Assert.That(loudSink.Count).IsEqualTo(0);

        var hostile = new NyraExtension(new LoudBridge { Logger = new ThrowingLogger() });
        await Assert
            .That(async () =>
            {
                await hostile.DisposeAsync().ConfigureAwait(false);
            })
            .Throws<InvalidOperationException>()
            .WithMessageContaining("generation storm: the loud bridge dies");

        var rig = await RigAsync().ConfigureAwait(false);
        try
        {
            rig.Server.Dispose();
            await Task.Delay(300).ConfigureAwait(false);
            await Assert
                .That(async () => await rig.Extension.DisposeAsync().ConfigureAwait(false))
                .ThrowsNothing();
        }
        finally
        {
            rig.Server.Dispose();
        }

        for (var round = 0; round < 10; round++)
        {
            var stormRig = await RigAsync().ConfigureAwait(false);
            try
            {
                var pushing = Task.Run(async () =>
                {
                    for (var frame = 0; frame < 250; frame++)
                    {
                        try
                        {
                            await stormRig
                                .Server.PushAsync(
                                    $$"""{"op":"event","guildId":"777","type":"VolumeChangedEvent","volume":{{50 + frame}}}"""
                                )
                                .ConfigureAwait(false);
                        }
                        catch { }
                        await Task.Delay(3).ConfigureAwait(false);
                    }
                });
                await Assert
                    .That(async () =>
                    {
                        var asyncDispose = Task.Run(async () =>
                            await stormRig.Extension.DisposeAsync().ConfigureAwait(false)
                        );
                        await Task.Delay(30).ConfigureAwait(false);
                        var drop = Task.Run(() =>
                            stormRig.Server.DropConnectionAsync(
                                4006,
                                $"generation storm: race {round}"
                            )
                        );
                        stormRig.Extension.Dispose();
                        await Task.WhenAll(asyncDispose, drop).ConfigureAwait(false);
                    })
                    .ThrowsNothing();
                await pushing.ConfigureAwait(false);
            }
            finally
            {
                stormRig.Server.Dispose();
            }
        }
    }

    [Test]
    public async Task SyncDispose()
    {
        var loudSink = new List<string>();
        var loud = new NyraExtension(new LoudBridge { Logger = new ListLogger(loudSink) });
        await Assert
            .That(() =>
            {
                loud.Dispose();
            })
            .Throws<InvalidOperationException>()
            .WithMessageContaining("generation storm: the loud bridge dies");
        await Assert.That(loudSink.Count).IsEqualTo(0);

        var asyncSink = new List<string>();
        var asyncThrower = new NyraExtension(
            new AsyncThrowerBridge { Logger = new ListLogger(asyncSink) }
        );
        await Assert
            .That(() =>
            {
                asyncThrower.Dispose();
            })
            .Throws<InvalidOperationException>()
            .WithMessageContaining("generation storm: the async bridge dies");
        await Assert.That(asyncSink.Count).IsEqualTo(0);

        var plain = new NyraExtension(new PlainBridge());
        await Assert.That(() => plain.Dispose()).ThrowsNothing();

        var rig = await RigAsync().ConfigureAwait(false);
        try
        {
            rig.Server.Dispose();
            await Task.Delay(300).ConfigureAwait(false);
            await Assert.That(() => rig.Extension.Dispose()).ThrowsNothing();
        }
        finally
        {
            rig.Server.Dispose();
        }
    }

    [Test]
    public async Task VoiceStorm()
    {
        var rig = await RigAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var errors = 0L;
            rig.Node.Error += (_, _) =>
            {
                Interlocked.Increment(ref errors);
                return Task.CompletedTask;
            };
            const int threads = 16;
            const int rounds = 40;
            var gate = new Barrier(threads);
            var workers = new Task[threads];
            for (var i = 0; i < threads; i++)
            {
                var slot = i;
                workers[i] = Task.Run(async () =>
                {
                    gate.SignalAndWait();
                    for (var round = 0; round < rounds; round++)
                    {
                        rig.Bridge.FireVoiceState(777, 555, $"vsess-{slot}-{round}");
                        rig.Bridge.FireVoiceServer(777, $"tok-{slot}-{round}", "127.0.0.1");
                        await Task.Yield();
                    }
                });
            }
            var storm = Task.WhenAll(workers);
            var finished =
                await Task.WhenAny(storm, Task.Delay(TimeSpan.FromSeconds(20)))
                    .ConfigureAwait(false) == storm;
            await Assert.That(finished).IsTrue();
            await Assert
                .That(() => rig.Bridge.VoiceSends.Any(s => s.GuildId == 777))
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromMilliseconds(10_000));
            await Task.Delay(500).ConfigureAwait(false);
            await Assert.That(errors).IsEqualTo(0);
            await Assert.That(rig.Connection.IsConnected).IsTrue();
        }

        var multiRig = await RigAsync(multiGuild: true).ConfigureAwait(false);
        await using (multiRig.ConfigureAwait(false))
        {
            var errors = 0L;
            multiRig.Node.Error += (_, _) =>
            {
                Interlocked.Increment(ref errors);
                return Task.CompletedTask;
            };
            const int guilds = 12;
            var connects = new List<Task<GuildConnection>>();
            for (var guild = 0; guild < guilds; guild++)
            {
                var guildId = 900_000UL + (ulong)guild;
                var connect = multiRig.Node.ConnectAsync(guildId, 555);
                connects.Add(connect);
            }
            await Task.Delay(200).ConfigureAwait(false);
            var all = Task.WhenAll(connects);
            var storm = Task.Run(async () =>
            {
                var round = 0;
                while (!all.IsCompleted && round < 400)
                {
                    for (var guild = 0; guild < guilds; guild++)
                    {
                        var guildId = 900_000UL + (ulong)guild;
                        multiRig.Bridge.FireVoiceState(guildId, 555, $"vsess-{round}");
                        multiRig.Bridge.FireVoiceServer(guildId, $"tok-{round}", "127.0.0.1");
                    }
                    round++;
                    await Task.Delay(20).ConfigureAwait(false);
                }
            });
            var allConnected = Task.WhenAll(connects);
            var landed =
                await Task.WhenAny(allConnected, Task.Delay(TimeSpan.FromSeconds(15)))
                    .ConfigureAwait(false) == allConnected;
            var stormDone =
                await Task.WhenAny(storm, Task.Delay(TimeSpan.FromSeconds(15)))
                    .ConfigureAwait(false) == storm;
            await Assert.That(stormDone).IsTrue();
            await Assert.That(landed).IsTrue();
            await Assert.That(connects.All(c => c.IsCompletedSuccessfully)).IsTrue();
            await Assert.That(connects.All(c => c.Result.IsConnected)).IsTrue();
            await Assert.That(errors).IsEqualTo(0);
        }
    }

    [Test]
    public async Task ReconnectStorm()
    {
        var rig = await RigAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var track = ParseTrack();
            await PlayAsync(rig, track).ConfigureAwait(false);
            var survived = true;
            for (var round = 0; round < 4; round++)
            {
                try
                {
                    await rig
                        .Server.DropConnectionAsync(4006, $"generation storm: storm {round}")
                        .ConfigureAwait(false);
                    await rig
                        .Server.PushAsync(
                            """{"op":"event","guildId":"777","type":"VolumeChangedEvent","volume":70}"""
                        )
                        .ConfigureAwait(false);
                    await rig.Server.PushAsync("""{"op":"no-such-op"}""").ConfigureAwait(false);
                }
                catch
                {
                    survived = false;
                }
                await Task.Delay(250).ConfigureAwait(false);
            }
            await Assert.That(survived).IsTrue();
            await Assert
                .That(() => Volatile.Read(ref rig.Server.WebsocketConnections) >= 5)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromMilliseconds(15_000));
            await PushVolumeAsync(rig, 64).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Volume == 64)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromMilliseconds(10_000));
        }
    }
}
