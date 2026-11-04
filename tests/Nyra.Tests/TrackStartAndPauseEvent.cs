using System.Text.Json;
using Nyra.Entities;

namespace Nyra.Tests;

public sealed class TrackStartAndPauseEvent
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private const string OtherTrackJson =
        """{"encoded":"b3RoZXI=","info":{"identifier":"otherid","author":"Other - Topic","title":"Other","length":90000,"isStream":false,"isSeekable":true,"position":0}}""";

    private static readonly JsonSerializerOptions Options = NyraJson.SerializerOptions;

    private static Track ParseTrack() => JsonSerializer.Deserialize<Track>(TrackJson, Options)!;

    private sealed class Rig : IAsyncDisposable
    {
        public required FakeBridge Bridge { get; init; }
        public required NyraExtension Extension { get; init; }
        public required Node Node { get; init; }
        public required FakeNodeServer Server { get; init; }
        public required GuildConnection Connection { get; init; }
        public required List<string> Reply { get; init; }
        public required List<TaskCompletionSource?> Gate { get; init; }
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

    private static string PlayerBody(string? trackJson, bool paused = false, int volume = 100) =>
        $$"""
            {
                "guildId": "777",
                "track": {{trackJson ?? "null"}},
                "volume": {{volume}},
                "paused": {{(paused ? "true" : "false")}},
                "state": null,
                "voice": {"token": "tok", "endpoint": "127.0.0.1", "sessionId": "vsess"}
            }
            """;

    private static async Task<Rig> RigAsync(bool lavalink = false)
    {
        var server = new FakeNodeServer
        {
            IsNodeLink = !lavalink,
            Semver = lavalink ? "4.2.0" : "3.9.1",
        };
        var holder = new List<string> { PlayerBody(null) };
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
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Nyra.Endpoint("127.0.0.1", server.Port, server.Password),
            new Nyra.NodeOptions
            {
                Name = "track-start",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
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

    private static async Task PlayAsync(Rig rig, string trackJson)
    {
        await Reflect.NextTickAsync().ConfigureAwait(false);
        rig.Reply[0] = PlayerBody(trackJson);
        var track = JsonSerializer.Deserialize<Track>(trackJson, Options)!;
        await rig.Connection.PlayAsync(track).ConfigureAwait(false);
        await Assert
            .That(() => rig.Connection.CurrentTrack?.Encoded)
            .WaitsFor(assert => assert.IsEqualTo(track.Encoded), TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task TrackStart()
    {
        var rig = await RigAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await PlayAsync(rig, TrackJson).ConfigureAwait(false);
            await rig
                .Server.PushAsync(
                    """{"op":"playerUpdate","guildId":"777","state":{"time":10,"position":5000,"connected":false,"ping":42}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds == 5000)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await rig
                .Server.PushAsync(
                    $$"""{"op":"event","guildId":"777","type":"TrackStartEvent","track":{{OtherTrackJson}}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds is >= 0 and <= 300)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            await Assert.That(rig.Connection.CurrentTrack?.Encoded).IsEqualTo("b3RoZXI=");
        }
    }

    [Test]
    public async Task PauseHold()
    {
        var rig = await RigAsync(lavalink: true).ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            await PlayAsync(rig, TrackJson).ConfigureAwait(false);
            rig.Reply[0] = PlayerBody(TrackJson, paused: true);
            await rig.Connection.PauseAsync().ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.IsPaused)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await rig
                .Server.PushAsync(
                    $$"""{"op":"event","guildId":"777","type":"TrackStartEvent","track":{{TrackJson}}}"""
                )
                .ConfigureAwait(false);
            await Task.Delay(400).ConfigureAwait(false);
            await Assert.That(rig.Connection.IsPaused).IsTrue();

            rig.Reply[0] = PlayerBody(TrackJson);
            await rig.Connection.PlayAsync(ParseTrack()).ConfigureAwait(false);
            await rig
                .Server.PushAsync(
                    $$"""{"op":"event","guildId":"777","type":"TrackStartEvent","track":{{TrackJson}}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(() => !rig.Connection.IsPaused)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task TeardownCapture()
    {
        var rig = await RigAsync().ConfigureAwait(false);
        try
        {
            await PlayAsync(rig, TrackJson).ConfigureAwait(false);
            rig.Reply[0] = PlayerBody(OtherTrackJson, volume: 77);
            var calls = rig.RestCalls;
            rig.GetGate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            var refresh = rig.Connection.RefreshAsync();
            await Assert
                .That(() => rig.RestCalls > calls)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
            var disconnect = rig.Connection.DisconnectAsync();
            rig.GetGate?.TrySetResult();
            await Assert
                .That(async () =>
                {
                    await Task.WhenAll(refresh, disconnect).ConfigureAwait(false);
                })
                .ThrowsNothing();
            await Assert.That(rig.Connection.CurrentTrack).IsNull();
        }
        finally
        {
            await rig.DisposeAsync().ConfigureAwait(false);
        }
    }
}
