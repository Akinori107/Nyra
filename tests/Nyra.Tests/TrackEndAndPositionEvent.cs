using System.Text.Json;
using Nyra.Entities;

namespace Nyra.Tests;

public sealed class TrackEndAndPositionEvent
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private const string EmptyTrackJson =
        """{"encoded":"","info":{"identifier":"","author":"","title":"","length":0,"isStream":false,"isSeekable":true,"position":0}}""";

    private static readonly JsonSerializerOptions Options = NyraJson.SerializerOptions;

    private static Track ParseTrack() => JsonSerializer.Deserialize<Track>(TrackJson, Options)!;

    private static async Task<Rig> RigAsync()
    {
        var holder = new Holder();
        var server = new FakeNodeServer();
        server.On(
            "PATCH",
            "/v4/sessions/fake-session/players/777",
            _ => new FakeRestResponse(200, holder.Reply)
        );
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Nyra.Endpoint("127.0.0.1", server.Port, server.Password),
            new Nyra.NodeOptions
            {
                Name = "track-end",
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
        return new Rig(bridge, extension, node, server, connection, holder);
    }

    private sealed class Holder
    {
        public string Reply { get; set; } =
            """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}""";
    }

    private sealed record Rig(
        FakeBridge Bridge,
        NyraExtension Extension,
        Node Node,
        FakeNodeServer Server,
        GuildConnection Connection,
        Holder Reply
    ) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private static string PlayerReply(string? trackBody) =>
        """{"guildId":"777","track":"""
        + (trackBody ?? "null")
        + ""","volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}""";

    private static async Task PlayAsync(Rig rig, Track track, string trackBody)
    {
        await Reflect.NextTickAsync().ConfigureAwait(false);
        rig.Reply.Reply = PlayerReply(trackBody);
        await rig.Connection.PlayAsync(track).ConfigureAwait(false);
        await Assert
            .That(() => rig.Connection.CurrentTrack?.Encoded)
            .WaitsFor(assert => assert.IsEqualTo(track.Encoded), TimeSpan.FromSeconds(5));
    }

    private static Task PushEndAsync(Rig rig, string? trackBody, string reason) =>
        rig.Server.PushAsync(
            """{"op":"event","guildId":"777","type":"TrackEndEvent","track":"""
                + (trackBody ?? "null")
                + ",\"reason\":\""
                + reason
                + "\"}"
        );

    private static Task PushUpdateAsync(Rig rig, long time, long position, bool connected) =>
        rig.Server.PushAsync(
            """{"op":"playerUpdate","guildId":"777","state":{"time":"""
                + time
                + ",\"position\":"
                + position
                + ",\"connected\":"
                + (connected ? "true" : "false")
                + ",\"ping\":20}}"
        );

    private static Task PushSeekAsync(Rig rig, long position) =>
        rig.Server.PushAsync(
            """{"op":"event","guildId":"777","type":"SeekEvent","position":""" + position + "}"
        );

    [Test]
    public async Task TrackEnd()
    {
        var rig = await RigAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var track = ParseTrack();
            await PlayAsync(rig, track, TrackJson).ConfigureAwait(false);

            await PushEndAsync(rig, TrackJson, "replaced").ConfigureAwait(false);
            await Task.Delay(150).ConfigureAwait(false);
            await Assert.That(rig.Connection.CurrentTrack).IsNotNull();

            await PushEndAsync(rig, TrackJson, "finished").ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.CurrentTrack is null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await PlayAsync(rig, track, TrackJson).ConfigureAwait(false);
            await PushEndAsync(rig, TrackJson, "replaced").ConfigureAwait(false);
            await Task.Delay(150).ConfigureAwait(false);
            await PushEndAsync(rig, EmptyTrackJson, "cleanup").ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.CurrentTrack is null)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task Position()
    {
        var rig = await RigAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var track = ParseTrack();

            await PushUpdateAsync(rig, 1000, 500, true).ConfigureAwait(false);
            await Task.Delay(150).ConfigureAwait(false);
            await Assert.That(rig.Connection.Position).IsNull();

            await PlayAsync(rig, track, TrackJson).ConfigureAwait(false);
            await PushUpdateAsync(rig, 2000, 700, true).ConfigureAwait(false);
            await Assert
                .That(() => (rig.Connection.Position?.TotalMilliseconds ?? 0) >= 700)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await PushUpdateAsync(rig, 2500, 800, false).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds == 800)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await PushUpdateAsync(rig, 1500, 999, true).ConfigureAwait(false);
            await Task.Delay(150).ConfigureAwait(false);
            await Assert.That(rig.Connection.Position?.TotalMilliseconds).IsEqualTo(800);

            await PushSeekAsync(rig, 1500).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds == 1500)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));

            await PushSeekAsync(rig, -5).ConfigureAwait(false);
            await Assert
                .That(() => rig.Connection.Position?.TotalMilliseconds == 0)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(5));
        }
    }
}
