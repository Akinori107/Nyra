using System.Text.Json;
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
        server.On(
            "GET",
            "/v4/loadtracks",
            call => new FakeRestResponse(200, $$"""{"loadType":"track","data":{{TrackJson}}}""")
        );

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

            server.On(
                "GET",
                "/v4/loadtracks",
                call => new FakeRestResponse(200, $$"""{"loadType":"track","data":{{TrackJson}}}""")
            );
            var urlResult = await node.LoadTracksAsync("https://example.com/audio.mp3?t=15")
                .ConfigureAwait(false);
            await Assert
                .That(
                    urlResult is LoadResult.TrackLoaded { Track.StartPosition: { } start }
                        && start == TimeSpan.FromSeconds(15)
                )
                .IsTrue();

            var bareServer = new FakeNodeServer();
            var bareStack = await StartAsync(bareServer).ConfigureAwait(false);
            await using (bareStack.ConfigureAwait(false))
            {
                bareStack.Server.On(
                    "GET",
                    "/v4/loadtracks",
                    call => new FakeRestResponse(
                        200,
                        $$"""{"loadType":"track","data":{{TrackJson}}}"""
                    )
                );
                var bareLoaded = await bareStack
                    .Node.LoadTracksAsync("atwa", SearchType.Spotify)
                    .ConfigureAwait(false);
                await Assert.That(bareLoaded is LoadResult.TrackLoaded).IsTrue();

                var lavaServer = new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" };
                var lavaStack = await StartAsync(lavaServer).ConfigureAwait(false);
                await using (lavaStack.ConfigureAwait(false))
                {
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
    }

    [Test]
    public async Task CodecRoutes()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);

            server.On(
                "POST",
                "/v4/decodetracks",
                call => new FakeRestResponse(200, $"[{TrackJson},{TrackJson}]")
            );
            var decoded = await node.Tracks.DecodeManyAsync(["one", "two"]).ConfigureAwait(false);
            await Assert.That(decoded.Count).IsEqualTo(2);

            await Assert
                .That(async () =>
                {
                    await node.Tracks.DecodeManyAsync([]).ConfigureAwait(false);
                })
                .Throws<ArgumentException>();

            await Assert
                .That(async () =>
                {
                    await node.Tracks.DecodeManyAsync(["one", " "]).ConfigureAwait(false);
                })
                .Throws<ArgumentException>();

            server.On(
                "POST",
                "/v4/encodedtracks",
                _ => new FakeRestResponse(200, "[\"QXRsZWFzdA==\"]")
            );
            var info = new TrackInfo
            {
                Identifier = "ph8qt3dhvwo",
                Author = "SoAD - Topic",
                Title = "ATWA",
                LengthMilliseconds = 176000,
            };
            var encoded = await node.Tracks.EncodeAsync(info).ConfigureAwait(false);
            await Assert.That(encoded).IsEqualTo("QXRsZWFzdA==");

            server.On(
                "POST",
                "/v4/encodedtracks",
                _ => new FakeRestResponse(200, "[\"QXRsZWFzdA==\",\"QXRsZWFzdB==\"]")
            );
            var batch = await node.Tracks.EncodeManyAsync([info, info]).ConfigureAwait(false);
            await Assert.That(batch.Count).IsEqualTo(2);

            await Assert
                .That(async () =>
                {
                    await node.Tracks.EncodeManyAsync([]).ConfigureAwait(false);
                })
                .Throws<ArgumentException>();
            await Assert
                .That(async () =>
                {
                    await node.Tracks.EncodeManyAsync([info, null!]).ConfigureAwait(false);
                })
                .Throws<ArgumentException>();

            var freed = new TaskCompletionSource<string>();
            server.On(
                "POST",
                "/v4/routeplanner/free/address",
                call =>
                {
                    freed.TrySetResult(call.Body ?? string.Empty);
                    return new FakeRestResponse(204, string.Empty);
                }
            );
            await node.RoutePlanner.FreeAddressAsync("/1.2.3.4").ConfigureAwait(false);
            await Assert
                .That(await freed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .Contains("1.2.3.4");
            await Assert.That(await freed.Task.ConfigureAwait(false)).DoesNotContain("/1");
            var freedAll = false;
            server.On(
                "POST",
                "/v4/routeplanner/free/all",
                _ => new FakeRestResponse(204, string.Empty)
            );
            await node.RoutePlanner.FreeAllAddressesAsync().ConfigureAwait(false);
            freedAll = server.RestCalls.Any(c =>
                c is { Method: "POST", Path: "/v4/routeplanner/free/all" }
            );
            await Assert.That(freedAll).IsTrue();
        }
    }

    [Test]
    public async Task LoadStreamGuards()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;
            var track = JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.SerializerOptions)!;

            using (Assert.Multiple())
            {
                await Assert
                    .That(async () =>
                        await node.Tracks.OpenAudioStreamAsync(track, -1).ConfigureAwait(false)
                    )
                    .Throws<ArgumentOutOfRangeException>();
                await Assert
                    .That(async () =>
                        await node.Tracks.OpenAudioStreamAsync(track, 1001).ConfigureAwait(false)
                    )
                    .Throws<ArgumentOutOfRangeException>();
                await Assert
                    .That(async () =>
                        await node
                            .Tracks.OpenAudioStreamAsync(track, 100, TimeSpan.FromSeconds(-1))
                            .ConfigureAwait(false)
                    )
                    .Throws<ArgumentOutOfRangeException>();
                await Assert
                    .That(async () =>
                        await node
                            .Tracks.OpenAudioStreamAsync(track, 100, format: (LoadStreamFormat)99)
                            .ConfigureAwait(false)
                    )
                    .Throws<ArgumentOutOfRangeException>();
            }
            await Assert
                .That(async () =>
                    await node
                        .Tracks.OpenAudioStreamAsync(
                            track,
                            100,
                            filters: new FilterChain { Volume = 2 }
                        )
                        .ConfigureAwait(false)
                )
                .Throws<NotSupportedException>();

            stack.Server.On("GET", "/v4/loadstream", _ => new FakeRestResponse(200, "audio-bytes"));
            var stream = await node
                .Tracks.OpenAudioStreamAsync(track, 100, TimeSpan.FromMilliseconds(500))
                .ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync().ConfigureAwait(false);
                await Assert.That(content).IsEqualTo("audio-bytes");
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

            server.On(
                "GET",
                "/v4/sessions/fake-session/groups/grp",
                _ => new FakeRestResponse(404, "{}")
            );
            await Assert.That(await node.Groups.GetAsync("grp").ConfigureAwait(false)).IsNull();
            server.On(
                "GET",
                "/v4/sessions/fake-session/groups/grp",
                _ => new FakeRestResponse(200, """{"id":"grp","guildIds":[777],"createdAt":100}""")
            );
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

            server.On(
                "PATCH",
                "/v4/sessions/fake-session/groups/grp",
                _ => new FakeRestResponse(200, """{"id":"grp","guildIds":[777],"createdAt":100}""")
            );
            var result = await node
                .Groups.UpdateAsync("grp", new GroupUpdate { Volume = 150, Paused = true })
                .ConfigureAwait(false);
            await Assert.That(result).IsNotNull();
            await Assert.That(result!.Id).IsEqualTo("grp");
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
    public async Task MeaningRead()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);
            server.On(
                "GET",
                "/v4/meaning",
                _ => new FakeRestResponse(
                    200,
                    """{"loadType":"meaning","data":{"source":"genius","meaning":"about love"}}"""
                )
            );
            var track = JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.SerializerOptions)!;
            var meaning = await node.Lyrics.LoadMeaningAsync(track, "en").ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(meaning).IsNotNull();
                await Assert.That(meaning!.LoadType).IsEqualTo(MeaningLoadType.Meaning);
                await Assert.That(meaning!.Data).IsNotNull();
            }
            var call = server.RestCalls.Last(c => c.Path == "/v4/meaning");
            await Assert.That(call.Query?.Contains("lang=en")).IsTrue();
        }
    }

    [Test]
    public async Task UrlPositionsAndTrims()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);

            ServeLoad(server);
            var marked = await node.LoadTracksAsync("\u200bytsearch:atwa\u200b")
                .ConfigureAwait(false);
            var markedCall = server.RestCalls.Last(c => c.Path == "/v4/loadtracks");
            await Assert.That(marked is LoadResult.TrackLoaded).IsTrue();
            await Assert.That(markedCall.Query?.Contains("ytsearch")).IsTrue();
            await Assert.That(markedCall.Query).DoesNotContain("%E2%80%8B");

            var plain = await node.LoadTracksAsync("plain query").ConfigureAwait(false);
            await Assert
                .That(plain is LoadResult.TrackLoaded { Track.StartPosition: null })
                .IsTrue();

            server.On(
                "GET",
                "/v4/loadtracks",
                _ => new FakeRestResponse(
                    200,
                    $$"""{"loadType":"search","data":[{{TrackJson}},{{TrackJson}}]}"""
                )
            );
            var search = await node.LoadTracksAsync("https://x.example/watch?list=a&t=90")
                .ConfigureAwait(false);
            await Assert
                .That(
                    search is LoadResult.SearchLoaded { } loaded
                        && loaded.Tracks.All(t => t.StartPosition == TimeSpan.FromSeconds(90))
                )
                .IsTrue();

            server.On(
                "GET",
                "/v4/loadtracks",
                _ => new FakeRestResponse(
                    200,
                    $$$"""{"loadType":"playlist","data":{"info":{"name":"mix","selectedTrack":1},"tracks":[{{{TrackJson}}},{{{TrackJson}}}]}}"""
                )
            );
            var playlist = await node.LoadTracksAsync("https://x.example/playlist?x=1&t=60")
                .ConfigureAwait(false);
            await Assert
                .That(
                    playlist is LoadResult.PlaylistLoaded { } mix
                        && mix.Tracks[0].StartPosition is null
                        && mix.Tracks[1].StartPosition == TimeSpan.FromSeconds(60)
                )
                .IsTrue();

            server.On(
                "GET",
                "/v4/loadtracks",
                _ => new FakeRestResponse(
                    200,
                    $$$"""{"loadType":"playlist","data":{"info":{"name":"mix","selectedTrack":5},"tracks":[{{{TrackJson}}},{{{TrackJson}}}]}}"""
                )
            );
            var orphan = await node.LoadTracksAsync("https://x.example/playlist?t=30")
                .ConfigureAwait(false);
            await Assert
                .That(
                    orphan is LoadResult.PlaylistLoaded { } orphaned
                        && orphaned.Tracks.All(t => t.StartPosition is null)
                )
                .IsTrue();

            server.On("GET", "/v4/meaning", 500, string.Empty);
            var track = JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.SerializerOptions)!;
            await Assert
                .That(async () =>
                {
                    await node.Lyrics.LoadMeaningAsync(track, "en").ConfigureAwait(false);
                })
                .Throws<Nyra.Exceptions.RestException>();
        }
    }
}
