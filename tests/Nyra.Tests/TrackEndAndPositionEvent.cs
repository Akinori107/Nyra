using System.Text.Json;
using Nyra.Entities;
using Nyra.EventArgs;
using Nyra.Internal;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class TrackEndAndPositionEvent
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private static readonly JsonSerializerOptions Options = NyraJson.SerializerOptions;

    private static Track ParseTrack() => JsonSerializer.Deserialize<Track>(TrackJson, Options)!;

    private static async Task<Rig> RigAsync()
    {
        var holder = new Holder();
        var server = new FakeNodeServer();
        server.On("PATCH", "/v4/sessions/fake-session/players/777", _ => new FakeRestResponse(
            200,
            holder.Reply
        ));
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
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
        await WaitForJoinAsync(bridge).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        var connection = await connect.ConfigureAwait(false);
        return new Rig(extension, server, connection, holder);
    }

    private sealed class Holder
    {
        public string Reply { get; set; } =
            """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}""";
    }

    private sealed record Rig(
        NyraExtension Extension,
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
        await Clock.NextTickAsync().ConfigureAwait(false);
        rig.Reply.Reply = PlayerReply(trackBody);
        await rig.Connection.PlayAsync(track).ConfigureAwait(false);
        await Assert
            .That(() => rig.Connection.CurrentTrack?.Encoded)
            .WaitsFor(assert => assert.IsEqualTo(track.Encoded), TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task AnEndWithoutATrackKeepsTheLastEndedOne()
    {
        var rig = await RigAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var track = ParseTrack();
            await PlayAsync(rig, track, TrackJson).ConfigureAwait(false);

            var requestTick = TimeProvider.System.GetMilliseconds();
            await Clock.NextTickAsync().ConfigureAwait(false);
            rig.Connection.Playback.OnTrackEnd(
                new TrackFinishedEventArgs { Track = track, EndReason = TrackEndReason.Finished }
            );
            rig.Connection.Playback.OnTrackEnd(
                new TrackFinishedEventArgs { Track = null, EndReason = TrackEndReason.Cleanup }
            );

            var stale = JsonSerializer.Deserialize<Player>(PlayerReply(TrackJson), Options)!;
            rig.Connection.CaptureState(stale, requestTick);

            await Assert.That(rig.Connection.CurrentTrack).IsNull();
        }
    }

    [Test]
    public async Task AnEndThatEndsNothingDoesNotDropALaterCapture()
    {
        var rig = await RigAsync().ConfigureAwait(false);
        await using (rig.ConfigureAwait(false))
        {
            var track = ParseTrack();
            await PlayAsync(rig, track, TrackJson).ConfigureAwait(false);
            rig.Connection.Playback.OnTrackEnd(
                new TrackFinishedEventArgs { Track = track, EndReason = TrackEndReason.Finished }
            );
            await PlayAsync(rig, track, TrackJson).ConfigureAwait(false);

            var requestTick = TimeProvider.System.GetMilliseconds();
            await Clock.NextTickAsync().ConfigureAwait(false);
            rig.Connection.Playback.OnTrackEnd(
                new TrackFinishedEventArgs { Track = null, EndReason = TrackEndReason.Cleanup }
            );

            var reply = JsonSerializer.Deserialize<Player>(
                PlayerReply(TrackJson)
                    .Replace("\"time\":0", "\"time\":1000", StringComparison.Ordinal)
                    .Replace("\"position\":0", "\"position\":5000", StringComparison.Ordinal),
                Options
            )!;
            rig.Connection.CaptureState(reply, requestTick);

            await Assert
                .That(rig.Connection.Position ?? TimeSpan.Zero)
                .IsGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(5000));
        }
    }
}
