using System.Text.Json;
using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class RestEdge
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    [Test]
    public async Task NoReplacePlay()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            server.On(
                "PATCH",
                PlayerPath,
                _ => new FakeRestResponse(
                    200,
                    """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
                )
            );

            await conn.PlayAsync(Track(), new PlayOptions { NoReplace = true })
                .ConfigureAwait(false);
            var call = server.RestCalls.Last(c => c.Method == "PATCH" && c.Path == PlayerPath);
            await Assert.That(call.Query).IsEqualTo("noReplace=true");

            await conn.PlayAsync(Track()).ConfigureAwait(false);
            var plain = server.RestCalls.Last(c => c.Method == "PATCH" && c.Path == PlayerPath);
            await Assert.That(plain.Query).IsNull();

            await conn.DisconnectAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task LoadStreamDeadNode()
    {
        var deadPort = DeadPort();
        var node = new Node(
            new Endpoint("127.0.0.1", deadPort, "unused"),
            new NodeOptions { Name = "dead-load" }
        );
        await Assert
            .That(async () =>
                await node
                    .Rest.Tracks.OpenLoadStreamAsync(
                        "QXRsZWFkdA==",
                        100,
                        0,
                        null,
                        LoadStreamFormat.Raw,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false)
            )
            .Throws<NodeUnreachableException>()
            .WithMessageContaining("dead-load");
    }

    private static async Task<Stack> StartAsync(FakeNodeServer? server = null)
    {
        server ??= new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "rest-edges",
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
            Bridge = bridge,
            Server = server,
        };
    }

    private static Track Track() =>
        JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.SerializerOptions)!;

    [Test]
    public async Task InfoWalk()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;

            stack.Server.InfoBody = "[1,2]";
            await Assert
                .That(async () =>
                {
                    await node.Rest.Info.GetServerInfoAsync(default).ConfigureAwait(false);
                })
                .Throws<IncompatibleServerException>();

            stack.Server.InfoBody = """{"isNodelink":{"nested":true},"version":"3.9.1"}""";
            var plainInfo = await node.Rest.Info.GetServerInfoAsync(default).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(plainInfo).IsNotNull();
                await Assert.That(plainInfo!.IsNodeLink).IsFalse();
                await Assert.That(plainInfo!.RawVersion).IsEqualTo("");
            }

            stack.Server.InfoBody = """{"version":{"semver":123}}""";
            plainInfo = await node.Rest.Info.GetServerInfoAsync(default).ConfigureAwait(false);
            await Assert.That(plainInfo?.RawVersion).IsEqualTo(string.Empty);

            stack.Server.InfoBody = """{"version":{"git":{"semver":"9.9.9"},"semver":"3.10.0"}}""";
            plainInfo = await node.Rest.Info.GetServerInfoAsync(default).ConfigureAwait(false);
            await Assert.That(plainInfo?.RawVersion).IsEqualTo("3.10.0");

            stack.Server.InfoBody = """{"filters":"nope"}""";
            plainInfo = await node.Rest.Info.GetServerInfoAsync(default).ConfigureAwait(false);
            await Assert.That(plainInfo?.EnabledFilters is { Count: 0 }).IsTrue();
            stack.Server.InfoBody = """{"filters":[123,"volume"]}""";
            plainInfo = await node.Rest.Info.GetServerInfoAsync(default).ConfigureAwait(false);
            await Assert
                .That(plainInfo?.EnabledFilters is { Count: 1 } set && set.Contains("volume"))
                .IsTrue();

            stack.Server.InfoBody = """{"plugins":"nope"}""";
            plainInfo = await node.Rest.Info.GetServerInfoAsync(default).ConfigureAwait(false);
            await Assert.That(plainInfo?.Plugins.Count).IsEqualTo(0);
            stack.Server.InfoBody = """{"plugins":[42]}""";
            plainInfo = await node.Rest.Info.GetServerInfoAsync(default).ConfigureAwait(false);
            await Assert.That(plainInfo?.Plugins.Count).IsEqualTo(0);
            stack.Server.InfoBody = """{"plugins":[{"name":42}]}""";
            plainInfo = await node.Rest.Info.GetServerInfoAsync(default).ConfigureAwait(false);
            await Assert.That(plainInfo?.Plugins.Count).IsEqualTo(0);
            stack.Server.InfoBody = """{"plugins":[{"other":1}]}""";
            plainInfo = await node.Rest.Info.GetServerInfoAsync(default).ConfigureAwait(false);
            await Assert.That(plainInfo?.Plugins.Count).IsEqualTo(0);

            stack.Server.InfoBody =
                """{"version":{"semver":"3.10.0","source":"s"},"isNodelink":true,"plugins":[{"name":"lavasrc-plugin"}],"filters":["volume"]}""";
            var full = await node.Rest.Info.GetServerInfoAsync(default).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(full).IsNotNull();
                await Assert.That(full!.IsNodeLink).IsTrue();
                await Assert.That(full!.RawVersion).IsEqualTo("3.10.0");
                await Assert.That(full!.EnabledFilters).IsNull();
                await Assert.That(full!.Plugins).Contains("lavasrc-plugin");
            }
        }
    }

    [Test]
    public async Task SessionIdGuards()
    {
        var stack = await StartAsync(new FakeNodeServer { SessionId = "bad id" })
            .ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;
            await Assert.That(node.IsReady).IsTrue();

            var guards = new Func<Task>[]
            {
                () => node.Rest.Lyrics.SubscribeAsync(777, false, default),
                () => node.Rest.Lyrics.UnsubscribeAsync(777, default),
                () => node.Rest.SponsorBlock.GetAsync(777, default),
                () => node.Rest.Mix.ListAsync(777, default),
                () => node.Rest.Mix.SetVolumeAsync(777, "layer-1", 0.5f, default),
                () => node.Rest.Players.ListAsync(default),
                () => node.Rest.Groups.ListAsync(default),
                () => node.Rest.Groups.GetAsync("g1", default),
                () => node.Rest.Players.GetAsync(777, default),
                () => node.Rest.Session.UpdateAsync(true, 60, default),
            };
            foreach (var call in guards)
            {
                await Assert
                    .That(async () =>
                    {
                        await call().ConfigureAwait(false);
                    })
                    .Throws<InvalidOperationException>();
            }
        }
    }

    [Test]
    public async Task RoutePlanner()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);
            const string path = "/v4/routeplanner/status";

            server.On("GET", path, 204, string.Empty);
            var unconfigured = await Assert
                .That(async () => await node.RoutePlanner.GetStatusAsync().ConfigureAwait(false))
                .Throws<RestException>();
            await Assert.That(unconfigured!.StatusCode).IsEqualTo(204);

            server.On("GET", path, 200, string.Empty);
            var empty = await Assert
                .That(async () => await node.RoutePlanner.GetStatusAsync().ConfigureAwait(false))
                .Throws<RestException>();
            await Assert.That(empty!.StatusCode).IsEqualTo(200);

            server.On("GET", path, 200, """{"class":"RotatingIpRoutePlanner"}""");
            var status = await node.RoutePlanner.GetStatusAsync().ConfigureAwait(false);
            await Assert.That(status?.Class).IsEqualTo(RoutePlannerClass.Rotating);

            var freed = new TaskCompletionSource<string?>();
            server.On(
                "POST",
                "/v4/routeplanner/free/address",
                call =>
                {
                    freed.TrySetResult(call.Body);
                    return new FakeRestResponse(204, string.Empty);
                }
            );
            await node.RoutePlanner.FreeAddressAsync("/1.0.0.0").ConfigureAwait(false);
            await Assert
                .That(
                    (
                        await freed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                    )?.Contains("\"1.0.0.0\"")
                )
                .IsTrue();
            await Assert
                .That(stack.Server.RestCalls)
                .Contains(c => c is { Method: "POST", Path: "/v4/routeplanner/free/address" });

            server.On("POST", "/v4/routeplanner/free/all", 204, string.Empty);
            await node.RoutePlanner.FreeAllAddressesAsync().ConfigureAwait(false);
            await Assert
                .That(stack.Server.RestCalls)
                .Contains(c => c is { Method: "POST", Path: "/v4/routeplanner/free/all" });
            await Assert
                .That(async () =>
                {
                    await node.RoutePlanner.FreeAddressAsync(" ").ConfigureAwait(false);
                })
                .Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task Lyrics()
    {
        var track = Track();

        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);
            server.On(
                "GET",
                "/v4/loadlyrics",
                _ => new FakeRestResponse(
                    200,
                    """{"loadType":"lyrics","data":{"name":"ATWA","synced":false,"text":"la"}}"""
                )
            );
            var lyrics = await node
                .Lyrics.LoadAsync(track, language: "pt", source: "musixmatch")
                .ConfigureAwait(false);
            await Assert
                .That(lyrics is { LoadType: LyricsLoadType.Lyrics, Data.Name: "ATWA" })
                .IsTrue();
            var call = server.RestCalls.Last(c => c.Path == "/v4/loadlyrics");
            await Assert.That(call.Query?.Contains("lang=pt")).IsTrue();
            await Assert.That(call.Query).Contains("source=musixmatch");

            server.On("GET", "/v4/loadlyrics", 200, "null");
            var emptyLyrics = await Assert
                .That(async () => await node.Lyrics.LoadAsync(track).ConfigureAwait(false))
                .Throws<RestException>();
            await Assert.That(emptyLyrics!.StatusCode).IsEqualTo(200);

            await Assert
                .That(async () =>
                {
                    await node.Lyrics.LoadAsync(null!).ConfigureAwait(false);
                })
                .Throws<ArgumentNullException>();
            await Assert
                .That(async () =>
                {
                    await node.Lyrics.LoadAsync(new Track()).ConfigureAwait(false);
                })
                .Throws<ArgumentException>();
        }

        var lavaServer = new FakeNodeServer
        {
            IsNodeLink = false,
            Semver = "4.2.0",
            InfoBody =
                """{"version":{"semver":"4.2.0"},"plugins":[{"name":"lavalyrics-plugin"}]}""",
        };
        var lavaStack = await StartAsync(lavaServer).ConfigureAwait(false);
        await using (lavaStack.ConfigureAwait(false))
        {
            var lavaNode = lavaStack.Node;
            lavaServer.On("GET", "/v4/lyrics", 200, string.Empty);
            var miss = await lavaNode
                .Lyrics.LoadAsync(track, skipTrackSource: true)
                .ConfigureAwait(false);
            await Assert.That(miss).IsNotNull();
            await Assert.That(miss!.LoadType).IsEqualTo(LyricsLoadType.Empty);
            var missCall = lavaServer.RestCalls.Last(c => c.Path == "/v4/lyrics");
            await Assert.That(missCall.Query?.Contains("skipTrackSource=true")).IsTrue();

            lavaServer.On(
                "GET",
                "/v4/lyrics",
                _ => new FakeRestResponse(
                    200,
                    """{"sourceName":"musixmatch","provider":"Musixmatch","text":"la","lines":[null,{"timestamp":1000,"duration":500,"line":"la"}]}"""
                )
            );
            var lavaLyrics = await lavaNode.Lyrics.LoadAsync(track).ConfigureAwait(false);
            await Assert
                .That(
                    lavaLyrics
                        is {
                            LoadType: LyricsLoadType.Lyrics,
                            Data.Synced: true,
                            Data.Provider: "Musixmatch",
                        }
                        && lavaLyrics.Data.Lines is [{ Text: "la" }]
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task Meaning()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);
            var track = Track();

            server.On(
                "GET",
                "/v4/meaning",
                _ => new FakeRestResponse(
                    200,
                    """{"loadType":"meaning","data":{"title":"ATWA","description":"d","paragraphs":["p"]}}"""
                )
            );
            var meaning = await node.Lyrics.LoadMeaningAsync(track, "pt").ConfigureAwait(false);
            await Assert
                .That(meaning is { LoadType: MeaningLoadType.Meaning, Data.Title: "ATWA" })
                .IsTrue();

            server.On("GET", "/v4/meaning", 500, """{"message":"provider down"}""");
            var failed = await node.Lyrics.LoadMeaningAsync(track).ConfigureAwait(false);
            await Assert
                .That(
                    failed
                        is { LoadType: MeaningLoadType.Error, Data.ErrorMessage: "provider down" }
                )
                .IsTrue();

            server.On("GET", "/v4/meaning", 500, "boom");
            var opaque = await Assert
                .That(async () => await node.Lyrics.LoadMeaningAsync(track).ConfigureAwait(false))
                .Throws<RestException>()
                .WithMessageContaining("boom");
            await Assert.That(opaque!.StatusCode).IsEqualTo(500);

            await Assert
                .That(async () =>
                {
                    await node.Lyrics.LoadMeaningAsync(null!).ConfigureAwait(false);
                })
                .Throws<ArgumentNullException>();
        }
    }

    [Test]
    public async Task Stream()
    {
        var oldStack = await StartAsync().ConfigureAwait(false);
        await using (oldStack.ConfigureAwait(false))
        {
            await Assert
                .That(async () =>
                {
                    await oldStack
                        .Node.Tracks.OpenAudioStreamAsync(Track(), format: LoadStreamFormat.Wav)
                        .ConfigureAwait(false);
                })
                .Throws<NotSupportedException>();
        }

        var stack = await StartAsync(new FakeNodeServer { Semver = "3.10.0" })
            .ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);
            var track = Track();

            server.On(
                "GET",
                "/v4/trackstream",
                _ => new FakeRestResponse(
                    200,
                    """{"url":"https://cdn/x","protocol":"http","format":"m4a","itag":140}"""
                )
            );
            var streamUrl = await node
                .Tracks.GetStreamUrlAsync(track, itag: 140)
                .ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(streamUrl).IsNotNull();
                await Assert.That(streamUrl!.Url).IsEqualTo("https://cdn/x");
                await Assert.That(streamUrl!.Protocol).IsEqualTo("http");
            }
            await Assert
                .That(
                    server
                        .RestCalls.Last(c => c.Path == "/v4/trackstream")
                        .Query?.Contains("itag=140")
                )
                .IsTrue();

            server.On("GET", "/v4/trackstream", 200, """{"protocol":"http"}""");
            var noUrl = await Assert
                .That(async () => await node.Tracks.GetStreamUrlAsync(track).ConfigureAwait(false))
                .Throws<RestException>();
            await Assert.That(noUrl!.StatusCode).IsEqualTo(200);

            await Assert
                .That(async () =>
                {
                    await node.Tracks.GetStreamUrlAsync(new Track()).ConfigureAwait(false);
                })
                .Throws<ArgumentException>();
            await Assert
                .That(async () =>
                {
                    await node.Tracks.GetStreamUrlAsync(track, itag: -1).ConfigureAwait(false);
                })
                .Throws<ArgumentOutOfRangeException>();

            server.On(
                "GET",
                "/v4/trackstream",
                200,
                """{"url":"https://cdn/zero","protocol":"http"}"""
            );
            var zeroItag = await node
                .Tracks.GetStreamUrlAsync(track, itag: 0)
                .ConfigureAwait(false);
            await Assert.That(zeroItag?.Url).IsEqualTo("https://cdn/zero");
            await Assert
                .That(server.RestCalls.Last(c => c.Path == "/v4/trackstream").Query)
                .Contains("itag=0");

            server.On("GET", "/v4/loadstream", 200, "PCM-DATA");
            var audio = await node
                .Tracks.OpenAudioStreamAsync(
                    track,
                    volume: 80,
                    position: TimeSpan.FromSeconds(1),
                    filters: new FilterChain
                    {
                        Tremolo = new TremoloFilter { Frequency = 8, Depth = 0.5f },
                    },
                    format: LoadStreamFormat.Wav
                )
                .ConfigureAwait(false);
            await using (audio.ConfigureAwait(false))
            {
                using var reader = new StreamReader(audio);
                var body = await reader.ReadToEndAsync().ConfigureAwait(false);
                await Assert.That(body).IsEqualTo("PCM-DATA");
                var loadCall = server.RestCalls.Last(c => c.Path == "/v4/loadstream");
                using (Assert.Multiple())
                {
                    await Assert.That(loadCall.Query?.Contains("volume=80")).IsTrue();
                    await Assert.That(loadCall.Query).Contains("position=1000");
                    await Assert.That(loadCall.Query).Contains("filters=");
                    await Assert.That(loadCall.Query).Contains("format=wav");
                }

                server.On("GET", "/v4/loadstream", 404, """{"message":"disabled"}""");
                var disabled = await Assert
                    .That(async () =>
                        await node.Tracks.OpenAudioStreamAsync(track).ConfigureAwait(false)
                    )
                    .Throws<RestException>();
                await Assert.That(disabled?.StatusCode).IsEqualTo(404);

                await Assert
                    .That(async () =>
                    {
                        await node.Tracks.OpenAudioStreamAsync(null!).ConfigureAwait(false);
                    })
                    .Throws<ArgumentNullException>();
                await Assert
                    .That(async () =>
                    {
                        await node
                            .Tracks.OpenAudioStreamAsync(
                                track,
                                position: TimeSpan.FromMilliseconds(-1)
                            )
                            .ConfigureAwait(false);
                    })
                    .Throws<ArgumentOutOfRangeException>();
            }
        }
    }

    [Test]
    public async Task ListNullGuards()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;

            stack.Server.On("GET", "/v4/sessions/fake-session/players", 200, "[null]");
            var nullPlayer = await Assert
                .That(async () => await node.Rest.Players.ListAsync(default).ConfigureAwait(false))
                .Throws<RestException>();
            await Assert.That(nullPlayer!.Path).Contains("/players");

            stack.Server.On("GET", PlayerPath + "/mix", 200, """{"mixes":[null]}""");
            var nullMix = await Assert
                .That(async () => await node.Rest.Mix.ListAsync(777, default).ConfigureAwait(false))
                .Throws<RestException>();
            await Assert.That(nullMix!.Path).Contains("/mix");
        }
    }

    [Test]
    public async Task PlayerNotFound()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;

            stack.Server.On("GET", PlayerPath, 404, """{"message":"Player not found"}""");
            await Assert
                .That(await node.Rest.Players.GetAsync(777, default).ConfigureAwait(false))
                .IsNull();

            stack.Server.On("GET", PlayerPath, 404, """{"message":"session 'x' not found"}""");
            var deadSession = await Assert
                .That(async () =>
                    await node.Rest.Players.GetAsync(777, default).ConfigureAwait(false)
                )
                .Throws<RestException>();
            await Assert.That(deadSession!.StatusCode).IsEqualTo(404);

            stack.Server.On("GET", PlayerPath, 404, "session gone plain");
            var plainSession = await Assert
                .That(async () =>
                    await node.Rest.Players.GetAsync(777, default).ConfigureAwait(false)
                )
                .Throws<RestException>()
                .WithMessageContaining("session gone plain");
            await Assert.That(plainSession!.ResponseBody).IsEqualTo("session gone plain");
            stack.Server.On("GET", PlayerPath, 404, "player gone plain");
            await Assert
                .That(await node.Rest.Players.GetAsync(777, default).ConfigureAwait(false))
                .IsNull();
        }
    }

    [Test]
    public async Task GroupUpdateGuards()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;

            var guards = new (GroupUpdate Update, Type Exception)[]
            {
                (
                    new GroupUpdate { PositionMilliseconds = 5000, EndTimeMilliseconds = 4000 },
                    typeof(ArgumentOutOfRangeException)
                ),
                (new GroupUpdate { Volume = -1 }, typeof(ArgumentOutOfRangeException)),
                (new GroupUpdate { Volume = 1001 }, typeof(ArgumentOutOfRangeException)),
                (
                    new GroupUpdate
                    {
                        Fading = new FadingOptions
                        {
                            Ducking = new DuckingOptions
                            {
                                Enabled = null,
                                DurationMilliseconds = null,
                                TargetVolume = 1.5f,
                                Curve = null,
                            },
                        },
                    },
                    typeof(ArgumentOutOfRangeException)
                ),
                (
                    new GroupUpdate { Filters = new FilterChain { Volume = 2f } },
                    typeof(NotSupportedException)
                ),
                (
                    new GroupUpdate { Track = new GroupTrack { Encoded = " " } },
                    typeof(ArgumentException)
                ),
                (
                    new GroupUpdate
                    {
                        Track = new GroupTrack { Encoded = "QXRsZWFzdA==", AudioTrackId = " " },
                    },
                    typeof(ArgumentException)
                ),
                (
                    new GroupUpdate
                    {
                        Track = new GroupTrack { Encoded = "QXRsZWFzdA==", Language = " " },
                    },
                    typeof(ArgumentException)
                ),
                (
                    new GroupUpdate
                    {
                        Track = new GroupTrack { Encoded = "Q", Identifier = "i" },
                    },
                    typeof(ArgumentException)
                ),
                (
                    new GroupUpdate { Track = new GroupTrack { Identifier = "i" } },
                    typeof(NotSupportedException)
                ),
                (
                    new GroupUpdate
                    {
                        Track = new GroupTrack
                        {
                            Encoded = "Q",
                            UserData = JsonDocument.Parse("[1]").RootElement,
                        },
                    },
                    typeof(ArgumentException)
                ),
            };
            foreach (var (update, exceptionType) in guards)
            {
                var rejected = await Assert
                    .That(async () =>
                        await node.Groups.UpdateAsync("g1", update).ConfigureAwait(false)
                    )
                    .Throws<Exception>();
                await Assert.That(rejected!.GetType()).IsEqualTo(exceptionType);
            }

            var patched = new TaskCompletionSource<string?>();
            stack.Server.On(
                "PATCH",
                "/v4/sessions/fake-session/groups/g1",
                call =>
                {
                    patched.TrySetResult(call.Body);
                    return new FakeRestResponse(
                        200,
                        """{"id":"g1","guildIds":[777],"createdAt":123}"""
                    );
                }
            );
            var result = await node
                .Groups.UpdateAsync("g1", new GroupUpdate { Paused = true, Volume = 100 })
                .ConfigureAwait(false);
            await Assert.That(result?.Id).IsEqualTo("g1");
            await Assert
                .That(
                    (
                        await patched.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                    )?.Contains("\"paused\":true")
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task YouTubeConfig()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);

            var guards = new (string? Token, string? Visitor)[]
            {
                (null, null),
                (null, " "),
                (" ", null),
            };
            foreach (var (token, visitor) in guards)
            {
                await Assert
                    .That(async () =>
                    {
                        await node.YouTube.UpdateConfigAsync(token, visitor).ConfigureAwait(false);
                    })
                    .Throws<ArgumentException>();
            }

            var patched = new TaskCompletionSource<string?>();
            server.On(
                "PATCH",
                "/v4/youtube/config",
                call =>
                {
                    patched.TrySetResult(call.Body);
                    return new FakeRestResponse(
                        200,
                        """{"message":"applied","workersUpdated":3,"fieldsUpdated":["refreshToken"]}"""
                    );
                }
            );
            var update = await node.YouTube.UpdateConfigAsync("rtok", "vdat").ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(update).IsNotNull();
                await Assert.That(update!.Message).IsEqualTo("applied");
                await Assert.That(update!.WorkersUpdated).IsEqualTo(3);
            }
            var patchBody = await patched
                .Task.WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            await Assert.That(patchBody?.Contains("rtok")).IsTrue();
            await Assert.That(patchBody).Contains("vdat");
        }
    }

    [Test]
    public async Task InfoTailAndRetryAfter()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;

            stack.Server.InfoBody =
                """{"version":{"semver":"3.10.0"},"isNodelink":true,"future":{"semver":"9.9.9"}}""";
            var withUnknown = await node
                .Rest.Info.GetServerInfoAsync(default)
                .ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(withUnknown).IsNotNull();
                await Assert.That(withUnknown!.IsNodeLink).IsTrue();
                await Assert.That(withUnknown!.RawVersion).IsEqualTo("3.10.0");
            }

            stack.Server.InfoBody = """{"version":{"semver":"3.10.0"}}{"junk":1}""";
            await Assert
                .That(async () =>
                {
                    await node.Rest.Info.GetServerInfoAsync(default).ConfigureAwait(false);
                })
                .Throws<IncompatibleServerException>();

            stack.Server.On("GET", PlayerPath, 404, string.Empty);
            await Assert
                .That(await node.Rest.Players.GetAsync(777, default).ConfigureAwait(false))
                .IsNull();

            stack.Server.InfoBody = null;
            stack.Server.On(
                "GET",
                "/v4/loadtracks",
                _ => new FakeRestResponse(429, """{"message":"slow down"}""", "2")
            );
            RestException? throttled = null;
            try
            {
                await node.Rest.Tracks.LoadAsync("atwa", default).ConfigureAwait(false);
            }
            catch (RestException exception)
            {
                throttled = exception;
            }
            await Assert.That(throttled?.RetryAfter).IsEqualTo(TimeSpan.FromSeconds(2));

            stack.Server.On(
                "GET",
                "/v4/loadtracks",
                _ => new FakeRestResponse(
                    429,
                    """{"message":"slow down"}""",
                    DateTimeOffset.UtcNow.AddSeconds(30).ToString("R")
                )
            );
            RestException? dated = null;
            try
            {
                await node.Rest.Tracks.LoadAsync("atwa", default).ConfigureAwait(false);
            }
            catch (RestException exception)
            {
                dated = exception;
            }
            await Assert
                .That(
                    dated?.RetryAfter is { } span
                        && span > TimeSpan.Zero
                        && span <= TimeSpan.FromSeconds(30)
                )
                .IsTrue();

            stack.Server.On(
                "GET",
                "/v4/loadtracks",
                _ => new FakeRestResponse(
                    429,
                    """{"message":"slow down"}""",
                    DateTimeOffset.UtcNow.AddSeconds(-30).ToString("R")
                )
            );
            RestException? stale = null;
            try
            {
                await node.Rest.Tracks.LoadAsync("atwa", default).ConfigureAwait(false);
            }
            catch (RestException exception)
            {
                stale = exception;
            }
            await Assert.That(stale?.RetryAfter).IsNull();

            stack.Server.On(
                "GET",
                "/v4/loadtracks",
                _ => new FakeRestResponse(429, """{"message":"slow down"}""", "banana")
            );
            RestException? junk = null;
            try
            {
                await node.Rest.Tracks.LoadAsync("atwa", default).ConfigureAwait(false);
            }
            catch (RestException exception)
            {
                junk = exception;
            }
            await Assert.That(junk?.RetryAfter).IsNull();
        }
    }
}
