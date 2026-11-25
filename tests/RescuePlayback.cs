using System.Text.Json;
using Nyra.Entities;
using Nyra.EventArgs;
using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class RescuePlayback
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0},"userData":{"requester":42}}""";

    private static string PlayerBody(bool paused) =>
        $$$"""
            {"guildId":"777","track":{{{TrackJson}}},"volume":60,"paused":{{{(
                paused ? "true" : "false"
            )}}},"filters":{"timescale":{"speed":1.25,"pitch":1.0,"rate":1.0}},"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}
            """;

    private static async Task<(
        FakeBridge Bridge,
        Node Node,
        FakeNodeServer Server,
        GuildConnection Conn,
        NyraExtension Extension
    )> ConnectAsync(
        bool rescue,
        TimeSpan? rescueTimeout = null,
        bool paused = true,
        bool nodeLink = true
    )
    {
        var server = new FakeNodeServer
        {
            ResumeOnReconnect = false,
            IsNodeLink = nodeLink,
            Semver = nodeLink ? "3.9.1" : "4.2.0",
        };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "rescue",
                RescuePlayback = rescue,
                RescueTimeout = rescueTimeout ?? TimeSpan.FromMinutes(3),
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(200, PlayerBody(paused)));
        return (bridge, node, server, conn, extension);
    }

    [Test]
    public async Task AStoppedNodeInvalidatesInsteadOfRestoring()
    {
        var (_, node, server, conn, extension) = await ConnectAsync(rescue: true)
            .ConfigureAwait(false);
        var invalidated = new TaskCompletionSource();
        var restored = false;
        conn.GuildConnectionInvalidated += (_, _) =>
        {
            invalidated.TrySetResult();
            return Task.CompletedTask;
        };
        conn.GuildConnectionRestored += (_, _) =>
        {
            restored = true;
            return Task.CompletedTask;
        };

        await node.DisconnectAsync().ConfigureAwait(false);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        await invalidated.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

        await Assert.That(restored).IsFalse();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    private static async Task<(GuildConnectionRestoredEventArgs Args, string[] Patches)> DropAsync(
        FakeNodeServer server,
        GuildConnection conn
    )
    {
        var restored = new TaskCompletionSource<GuildConnectionRestoredEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        conn.GuildConnectionRestored += (_, args) =>
        {
            restored.TrySetResult(args);
            return Task.CompletedTask;
        };
        var before = server.RestCalls.Count;
        await server.DropConnectionAsync(1000, "rescue").ConfigureAwait(false);
        var args = await restored.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        var patches = server
            .RestCalls.Skip(before)
            .Where(call => call.Method == "PATCH" && call.Path == PlayerPath)
            .Select(call => call.Body ?? string.Empty)
            .ToArray();
        return (args, patches);
    }

    private static async Task CleanUpAsync(
        FakeNodeServer server,
        GuildConnection conn,
        NyraExtension extension
    )
    {
        await conn.DisconnectAsync().ConfigureAwait(false);
        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ReconciledPlaybackRetainsOptionsDuringRecovery(
        bool identifier,
        bool noReplace
    )
    {
        var (_, _, server, conn, extension) = await ConnectAsync(rescue: true, nodeLink: false)
            .ConfigureAwait(false);
        try
        {
            await conn.PlayAsync(
                    JsonSerializer.Deserialize<Track>(TrackJson)!,
                    new PlayOptions(EndTime: TimeSpan.FromSeconds(30))
                )
                .ConfigureAwait(false);
            var other = TrackJson
                .Replace("QXRsZWFzdA==", "Yg==", StringComparison.Ordinal)
                .Replace("ph8qt3dhvwo", "other", StringComparison.Ordinal);
            var response = PlayerBody(true).Replace(TrackJson, other, StringComparison.Ordinal);
            var options = new PlayOptions(NoReplace: noReplace, EndTime: TimeSpan.FromSeconds(20));
            server.On("PATCH", PlayerPath, 200, "@");
            server.On("GET", PlayerPath, 200, response);
            if (identifier)
                await Assert
                    .That(async () =>
                        await conn.PlayIdentifierAsync("other", options).ConfigureAwait(false)
                    )
                    .Throws<RestException>();
            else
                await Assert
                    .That(() => conn.PlayAsync(JsonSerializer.Deserialize<Track>(other)!, options))
                    .Throws<RestException>();
            await Assert.That(conn.CurrentTrack?.Info.Identifier).IsEqualTo("other");
            server.On("PATCH", PlayerPath, 200, response);
            var (args, patches) = await DropAsync(server, conn).ConfigureAwait(false);
            await Assert.That(args.PlaybackResent).IsTrue();
            using var body = JsonDocument.Parse(
                patches.Single(patch => patch.Contains("track", StringComparison.Ordinal))
            );
            await Assert
                .That(body.RootElement.GetProperty("track").GetProperty("encoded").GetString())
                .IsEqualTo("Yg==");
            await Assert
                .That(body.RootElement.GetProperty("endTime").GetInt64())
                .IsEqualTo(20_000L);
        }
        finally
        {
            await CleanUpAsync(server, conn, extension).ConfigureAwait(false);
        }
    }
}
