using Nyra.Entities;

namespace Nyra.Tests;

public sealed class QuerySurface
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private sealed record Stack : IAsyncDisposable
    {
        public required NyraExtension Extension { get; init; }

        public required Node Node { get; init; }

        public required FakeNodeServer Server { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private static async Task<Stack> StartAsync(FakeNodeServer? server = null)
    {
        server ??= new FakeNodeServer
        {
            InfoBody =
                """{"version":{"semver":"3.10.0"},"isNodelink":true,"plugins":["lavasrc-plugin"],"filters":[]}""",
        };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "queries",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        return new Stack
        {
            Extension = extension,
            Node = node,
            Server = server,
        };
    }

    private static void ServeLoad(FakeNodeServer server) =>
        server.On("GET", "/v4/loadtracks", _ => new FakeRestResponse(
            200,
            $$"""{"loadType":"track","data":{{TrackJson}}}"""
        ));

    [Test]
    public async Task Search()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);
            ServeLoad(server);

            var cases = new (SearchType Type, string? Prefix)[]
            {
                (SearchType.YouTubeMusic, "ytmsearch"),
                (SearchType.SoundCloud, "scsearch"),
                (SearchType.Raw, null),
                (SearchType.Spotify, "spsearch"),
                (SearchType.SpotifyRecommendations, "sprec"),
                (SearchType.Monochrome, "monochrome"),
                (SearchType.Gaana, "gnsearch"),
                (SearchType.Bandcamp, "bcsearch"),
                (SearchType.IHeartRadio, "ihsearch"),
                (SearchType.YouTubeRecommendations, "ytrec"),
                (SearchType.QobuzRecommendations, "qbrec"),
                (SearchType.LetrasRecommendations, "lmrec"),
                (SearchType.EternalBox, "eternalbox"),
                (SearchType.JioSaavn, "jssearch"),
                (SearchType.JioSaavnRecommendations, "jsrec"),
                (SearchType.Qobuz, "qbsearch"),
                (SearchType.Tidal, "tdsearch"),
                (SearchType.TidalRecommendations, "tdrec"),
                (SearchType.Letras, "lmsearch"),
                (SearchType.Audius, "ausearch"),
                (SearchType.VkMusic, "vksearch"),
                (SearchType.VkMusicRecommendations, "vkrec"),
                (SearchType.AmazonMusic, "amazonmusic"),
                (SearchType.Bluesky, "bksearch"),
                (SearchType.AppleMusic, "amsearch"),
                (SearchType.NetEase, "ntsearch"),
                (SearchType.YandexMusic, "ymsearch"),
                (SearchType.YandexMusicRecommendations, "ymrec"),
                (SearchType.Bilibili, "bilisearch"),
                (SearchType.LastFm, "lfsearch"),
                (SearchType.GoogleDrive, "gdsearch"),
                (SearchType.Shazam, "shsearch"),
                (SearchType.Deezer, "dzsearch"),
                (SearchType.DeezerRecommendations, "dzrec"),
                (SearchType.Anghami, "agsearch"),
                (SearchType.Audiomack, "admsearch"),
                (SearchType.Pandora, "pdsearch"),
                (SearchType.NicoVideo, "ncsearch"),
                (SearchType.SongLink, "slsearch"),
                (SearchType.Flowery, "ftts"),
                (SearchType.GoogleTts, "gtts"),
                (SearchType.PiperTts, "pipertts"),
                (SearchType.LazyPyTts, "lazypytts"),
            };
            foreach (var (type, prefix) in cases)
            {
                var expected = prefix is null ? "atwa" : $"{prefix}:atwa";
                await node.LoadTracksAsync("atwa", type).ConfigureAwait(false);
                var call = server.RestCalls.Last(c => c.Path == "/v4/loadtracks");
                await Assert
                    .That(call.Query?.Contains("identifier=" + Uri.EscapeDataString(expected)))
                    .IsTrue();
            }

            server.On("GET", "/v4/loadtracks", _ => new FakeRestResponse(
                200,
                $$"""{"loadType":"track","data":{{TrackJson}}}"""
            ));
            var urlResult = await node.LoadTracksAsync("https://example.com/audio.mp3?t=15")
                .ConfigureAwait(false);
            await Assert
                .That(
                    urlResult is TrackLoaded { Track.StartPosition: { } start }
                        && start == TimeSpan.FromSeconds(15)
                )
                .IsTrue();

            var bareServer = new FakeNodeServer();
            var bareStack = await StartAsync(bareServer).ConfigureAwait(false);
            await using (bareStack.ConfigureAwait(false))
            {
                bareStack.Server.On("GET", "/v4/loadtracks", _ => new FakeRestResponse(
                    200,
                    $$"""{"loadType":"track","data":{{TrackJson}}}"""
                ));
                var bareLoaded = await bareStack
                    .Node.LoadTracksAsync("atwa", SearchType.Spotify)
                    .ConfigureAwait(false);
                await Assert.That(bareLoaded is TrackLoaded).IsTrue();

                var lavaServer = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
                var lavaStack = await StartAsync(lavaServer).ConfigureAwait(false);
                await using (lavaStack.ConfigureAwait(false))
                    await Assert
                        .That(async () =>
                        {
                            await lavaStack
                                .Node.LoadTracksAsync("atwa", SearchType.Spotify)
                                .ConfigureAwait(false);
                        })
                        .Throws<NotSupportedException>();
            }
        }
    }

    [Test]
    public async Task GroupSurface()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);

            await Assert
                .That(async () =>
                {
                    await node.Groups.GetAsync("bad id!").ConfigureAwait(false);
                })
                .Throws<ArgumentException>();

            server.On("GET", "/v4/sessions/fake-session/groups/grp", _ => new FakeRestResponse(
                404,
                "{}"
            ));
            await Assert.That(await node.Groups.GetAsync("grp").ConfigureAwait(false)).IsNull();
            server.On("GET", "/v4/sessions/fake-session/groups/grp", _ => new FakeRestResponse(
                200,
                """{"id":"grp","guildIds":[777],"createdAt":100}"""
            ));
            var group = await node.Groups.GetAsync("grp").ConfigureAwait(false);
            await Assert.That(group is { Id: "grp", GuildIds: [777] }).IsTrue();

            await Assert
                .That(async () =>
                {
                    await node
                        .Groups.UpdateAsync("grp", new GroupUpdate { PositionMilliseconds = -1 })
                        .ConfigureAwait(false);
                })
                .Throws<ArgumentOutOfRangeException>();
            await Assert
                .That(async () =>
                {
                    await node
                        .Groups.UpdateAsync("grp", new GroupUpdate { EndTimeMilliseconds = 0 })
                        .ConfigureAwait(false);
                })
                .Throws<ArgumentOutOfRangeException>();

            server.On("PATCH", "/v4/sessions/fake-session/groups/grp", _ => new FakeRestResponse(
                200,
                """{"id":"grp","guildIds":[777],"createdAt":100}"""
            ));
            var result = await node
                .Groups.UpdateAsync("grp", new GroupUpdate { Volume = 150, Paused = true })
                .ConfigureAwait(false);
            await Assert.That(result).IsNotNull();
            await Assert.That(result.Id).IsEqualTo("grp");
            var updateCall = server.RestCalls.Last(c =>
                c is { Method: "PATCH", Path: "/v4/sessions/fake-session/groups/grp" }
            );
            using (Assert.Multiple())
            {
                await Assert.That(updateCall.Body).IsNotNull();
                await Assert.That(updateCall.Body).Contains("\"volume\":150");
                await Assert.That(updateCall.Body).Contains("\"paused\":true");
            }
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(3)]
    public async Task ADecodeBatchWithMismatchedCardinalityIsRejected(int count)
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            stack.Server.On("POST", "/v4/decodetracks", _ => new FakeRestResponse(
                200,
                "[" + string.Join(",", Enumerable.Repeat(TrackJson, count)) + "]"
            ));
            await Assert
                .That(async () =>
                    await stack.Node.Tracks.DecodeManyAsync(["one", "two"]).ConfigureAwait(false)
                )
                .Throws<Nyra.Exceptions.RestException>();
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(3)]
    public async Task AnEncodeBatchWithMismatchedCardinalityIsRejected(int count)
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            stack.Server.On("POST", "/v4/encodedtracks", _ => new FakeRestResponse(
                200,
                "[" + string.Join(",", Enumerable.Repeat("\"QXRsZWFzdA==\"", count)) + "]"
            ));
            await Assert
                .That(async () =>
                    await stack
                        .Node.Tracks.EncodeManyAsync([new TrackInfo(), new TrackInfo()])
                        .ConfigureAwait(false)
                )
                .Throws<Nyra.Exceptions.RestException>();
        }
    }

    [Test]
    public async Task AResponseWithMultipleEncodedTracksIsRejectedForASingleEncode()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            stack.Server.On("POST", "/v4/encodedtracks", _ => new FakeRestResponse(
                200,
                "[\"QXRsZWFzdA==\",\"Yg==\"]"
            ));
            await Assert
                .That(async () =>
                    await stack.Node.Tracks.EncodeAsync(new TrackInfo()).ConfigureAwait(false)
                )
                .Throws<Nyra.Exceptions.RestException>();
        }
    }
}
