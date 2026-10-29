using System.Text.Json;
using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.EventArgs;
using Nyra.Exceptions;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class NodeIntegration
{
    private const string TrackJson =
        """{"encoded":"QXRsZWFzdA==","info":{"identifier":"ph8qt3dhvwo","author":"SoAD - Topic","title":"ATWA","length":176000,"isStream":false,"isSeekable":true,"position":0}}""";

    private static async Task<Stack> StartAsync(
        FakeNodeServer? server = null,
        NodeOptions? options = null
    )
    {
        server ??= new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            options
                ?? new NodeOptions
                {
                    Name = "fake",
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
    public async Task ConnectCycle()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, extension, node, server) = (
                stack.Bridge,
                stack.Extension,
                stack.Node,
                stack.Server
            );

            using (Assert.Multiple())
            {
                await Assert.That(node.IsConnected).IsTrue();
                await Assert.That(node.IsReady).IsTrue();
                await Assert.That(node.SessionId).IsEqualTo("fake-session");
                await Assert.That(node.IsNodeLink).IsTrue();
            }

            var headers = server.WebsocketHeaders.LastOrDefault() ?? [];
            await Assert.That(headers.TryGetValue("Authorization", out var auth)).IsTrue();
            await Assert.That(auth).IsEqualTo("youshallnotpass");
            await Assert.That(headers.TryGetValue("User-Id", out var user)).IsTrue();
            await Assert.That(user).IsEqualTo(FakeBridge.UserId.ToString());
            await Assert.That(headers.TryGetValue("Client-Name", out var label)).IsTrue();
            await Assert.That(label).IsEqualTo(Nyra.Internal.ClientHeaders.UserAgent);

            var statsReported = new TaskCompletionSource();
            extension.NodeStatsUpdated += (_, _) =>
            {
                statsReported.TrySetResult();
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"stats","players":1,"playingPlayers":1,"uptime":500,"memory":{"free":1,"used":2,"allocated":3,"reservable":4},"cpu":{"cores":2,"systemLoad":0.1,"lavalinkLoad":0.2}}"""
                )
                .ConfigureAwait(false);
            await Assert.That(statsReported.Task.Wait(TimeSpan.FromSeconds(5))).IsTrue();
        }
    }

    [Test]
    public async Task RestQueries()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;
            var server = stack.Server;

            server.On(
                "GET",
                "/v4/loadtracks",
                call => new FakeRestResponse(200, $$"""{"loadType":"track","data":{{TrackJson}}}""")
            );
            var loaded = await node.LoadTracksAsync("ytsearch:atwa").ConfigureAwait(false);
            await Assert
                .That(loaded is LoadResult.TrackLoaded { Track: { Encoded: "QXRsZWFzdA==" } })
                .IsTrue();
            var loadCall = server.RestCalls.FirstOrDefault(c => c.Path == "/v4/loadtracks");
            await Assert
                .That(
                    loadCall?.Query is { } query
                        && query.Contains("identifier=")
                        && query.Contains("atwa")
                )
                .IsTrue();

            server.On(
                "GET",
                "/v4/info",
                _ => new FakeRestResponse(
                    200,
                    """{"version":{"semver":"4.2.0","major":4,"minor":2,"patch":0},"git":{"branch":"main","commit":"abc"},"sourceManagers":["youtube"],"filters":["volume"],"isNodelink":false}"""
                )
            );
            var info = await node.GetNodeInfoAsync().ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(info).IsNotNull();
                await Assert.That(info!.Version).IsNotNull();
                await Assert.That(info!.Version!.Semver).IsEqualTo("4.2.0");
                await Assert.That(info!.Git).IsNotNull();
                await Assert.That(info!.Git!.Branch).IsEqualTo("main");
                await Assert.That(info!.IsNodeLink).IsFalse();
            }

            server.On(
                "GET",
                "/v4/stats",
                _ => new FakeRestResponse(
                    200,
                    """{"players":9,"playingPlayers":4,"uptime":1000,"memory":{"free":1,"used":2,"allocated":3,"reservable":4},"cpu":{"cores":8,"systemLoad":0.5,"lavalinkLoad":0.25},"detailedStats":{"api":{"requests":{"loadtracks":3}}}}"""
                )
            );
            var stats = await node.GetStatsAsync().ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(stats).IsNotNull();
                await Assert.That(stats!.Players).IsEqualTo(9);
                await Assert.That(stats!.DetailedStats).IsNotNull();
            }

            await Assert
                .That(
                    (await node.GetVersionAsync().ConfigureAwait(false))
                        ?.ToString()
                        .StartsWith("3.9")
                )
                .IsTrue();

            server.On("GET", "/v4/decodetrack", _ => new FakeRestResponse(200, TrackJson));
            var decoded = await node.Tracks.DecodeAsync("QXRsZWFzdA==").ConfigureAwait(false);
            await Assert.That(decoded.Encoded).IsEqualTo("QXRsZWFzdA==");

            server.On(
                "POST",
                "/v4/decodetracks",
                _ => new FakeRestResponse(200, $"[{TrackJson},{TrackJson}]")
            );
            var decodedList = await node
                .Tracks.DecodeManyAsync(["QXRsZWFzdA==", "QXRsZWFzdA=="])
                .ConfigureAwait(false);
            await Assert.That(decodedList.Count).IsEqualTo(2);

            server.On(
                "GET",
                "/v4/routeplanner/status",
                _ => new FakeRestResponse(
                    200,
                    """{"class":"RotatingIpRoutePlanner","details":{"ipBlock":{"type":"Inet4Address","size":"/8"},"failingAddresses":[]}}"""
                )
            );
            var planner = await node.RoutePlanner.GetStatusAsync().ConfigureAwait(false);
            await Assert.That(planner).IsNotNull();
            await Assert.That(planner!.Class).IsEqualTo(RoutePlannerClass.Rotating);

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
            await node.RoutePlanner.FreeAddressAsync("1.2.3.4").ConfigureAwait(false);
            await Assert
                .That(await freed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsEqualTo("""{"address":"1.2.3.4"}""");

            server.On(
                "GET",
                "/v4/youtube/config",
                _ => new FakeRestResponse(200, """{"isConfigured":true,"refreshToken":"***"}""")
            );
            var config = await node.YouTube.GetConfigAsync().ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(config).IsNotNull();
                await Assert.That(config!.IsConfigured).IsTrue();
                await Assert.That(config!.RefreshToken).IsEqualTo("***");
            }

            server.On(
                "PATCH",
                "/v4/youtube/config",
                _ => new FakeRestResponse(200, """{"message":"updated","workersUpdated":2}""")
            );
            var update = await node
                .YouTube.UpdateConfigAsync(refreshToken: "new-token")
                .ConfigureAwait(false);
            await Assert.That(update).IsNotNull();
            await Assert.That(update!.WorkersUpdated).IsEqualTo(2);

            server.On(
                "POST",
                "/v4/youtube/oauth",
                _ => new FakeRestResponse(
                    200,
                    """{"access_token":"tok","expires_in":3600,"token_type":"Bearer"}"""
                )
            );
            var token = await node.YouTube.RefreshTokenAsync("r").ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(token).IsNotNull();
                await Assert.That(token!.AccessToken).IsEqualTo("tok");
                await Assert.That(token!.ExpiresInSeconds).IsEqualTo(3600);
            }

            server.On(
                "PATCH",
                "/v4/sessions/fake-session",
                _ => new FakeRestResponse(200, """{"resuming":true,"timeout":120}""")
            );
            var session = await node.UpdateSessionAsync(
                    resuming: true,
                    timeout: TimeSpan.FromSeconds(120)
                )
                .ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(session).IsNotNull();
                await Assert.That(session!.Resuming).IsTrue();
                await Assert.That(session!.Timeout).IsEqualTo(120);
            }

            var connStatus = new TaskCompletionSource<string?>();
            server.On(
                "GET",
                "/v4/connection",
                call =>
                {
                    connStatus.TrySetResult(call.Authorization);
                    return new FakeRestResponse(200, """{"status":"good"}""");
                }
            );
            var connection = await node.GetConnectionStatusAsync().ConfigureAwait(false);
            await Assert.That(connection).IsNotNull();
            await Assert.That(connection!.Status).IsEqualTo(ConnectionQuality.Good);
        }
    }

    [Test]
    public async Task GroupsSurface()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;
            var server = stack.Server;

            server.On(
                "GET",
                "/v4/sessions/fake-session/groups",
                _ => new FakeRestResponse(
                    200,
                    """[{"id":"late-night","guildIds":["123","456"],"createdAt":1699999999999}]"""
                )
            );
            var groups = await node.Groups.ListAsync().ConfigureAwait(false);
            await Assert.That(groups).IsNotNull();
            await Assert.That(groups!.Count).IsEqualTo(1);
            await Assert.That(groups![0] is { Id: "late-night", GuildIds.Count: 2 }).IsTrue();

            server.On(
                "POST",
                "/v4/sessions/fake-session/groups",
                _ => new FakeRestResponse(201, """{"id":"new","guildIds":["123"],"createdAt":5}""")
            );
            var created = await node.Groups.CreateAsync("new", [123]).ConfigureAwait(false);
            await Assert.That(created).IsNotNull();
            await Assert.That(created!.Id).IsEqualTo("new");

            server.On(
                "PATCH",
                "/v4/sessions/fake-session/groups/late-night",
                call =>
                    call.Body is not null && call.Body.Contains("track")
                        ? new FakeRestResponse(
                            200,
                            """{"id":"late-night","guildIds":["123"],"createdAt":1,"players":[{"guildId":"123","volume":300}],"errors":[]}"""
                        )
                        : new FakeRestResponse(
                            200,
                            """{"id":"late-night","guildIds":["123","456"],"createdAt":1,"players":[],"errors":[]}"""
                        )
            );
            var updated = await node
                .Groups.UpdateAsync("late-night", new GroupUpdate { Volume = 300, Paused = true })
                .ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(updated).IsNotNull();
                await Assert.That(updated!.Id).IsEqualTo("late-night");
                await Assert.That(updated!.Errors.Count).IsEqualTo(0);
            }

            var played = await node
                .Groups.PlayTrackAsync(
                    "late-night",
                    JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.OptionsFor(true))!
                )
                .ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(played).IsNotNull();
                await Assert.That(played!.Players).IsNotNull();
                await Assert.That(played!.Players!.Count).IsEqualTo(1);
            }

            var deleted = new TaskCompletionSource<string?>();
            server.On(
                "DELETE",
                "/v4/sessions/fake-session/groups/late-night",
                call =>
                {
                    deleted.TrySetResult(call.Body);
                    return new FakeRestResponse(204, string.Empty);
                }
            );
            await node.Groups.DeleteAsync("late-night").ConfigureAwait(false);
            await Assert
                .That(await deleted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsNull();
        }
    }

    [Test]
    public async Task LyricsAndMeanings()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;
            var server = stack.Server;

            server.On(
                "GET",
                "/v4/loadlyrics",
                _ => new FakeRestResponse(
                    200,
                    """{"loadType":"lyrics","data":{"name":"ATWA","synced":true,"lines":[{"text":"line","time":100,"duration":200}],"provider":"musixmatch"}}"""
                )
            );
            var track = JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.OptionsFor(true))!;
            var lyrics = await node.Lyrics.LoadAsync(track).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(lyrics).IsNotNull();
                await Assert.That(lyrics!.LoadType).IsEqualTo(LyricsLoadType.Lyrics);
                await Assert.That(lyrics!.Data).IsNotNull();
                await Assert.That(lyrics!.Data!.Synced).IsTrue();
            }

            server.On(
                "GET",
                "/v4/loadchapters",
                _ => new FakeRestResponse(200, """[{"title":"Intro","start":0}]""")
            );
            var chapters = await node.Lyrics.LoadChaptersAsync(track).ConfigureAwait(false);
            await Assert.That(chapters).IsNotNull();
            await Assert.That(chapters!.Count).IsEqualTo(1);
            await Assert.That(chapters![0].Title).IsEqualTo("Intro");

            server.On(
                "GET",
                "/v4/meaning",
                _ => new FakeRestResponse(
                    200,
                    """{"loadType":"meaning","data":{"title":"About","paragraphs":["p"],"provider":"wikipedia"}}"""
                )
            );
            var meaning = await node.Lyrics.LoadMeaningAsync(track).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(meaning).IsNotNull();
                await Assert.That(meaning!.LoadType).IsEqualTo(MeaningLoadType.Meaning);
                await Assert.That(meaning!.Data).IsNotNull();
                await Assert.That(meaning!.Data!.Title).IsEqualTo("About");
            }
        }
    }

    [Test]
    public async Task VoiceAndPlayback()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);

            var playerPatch = new TaskCompletionSource<string?>();
            server.On(
                "PATCH",
                "/v4/sessions/fake-session/players/777",
                call =>
                {
                    playerPatch.TrySetResult(call.Body);
                    return new FakeRestResponse(
                        200,
                        """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}"""
                    );
                }
            );

            var connect = node.ConnectAsync(777, 555);
            await Task.Delay(100).ConfigureAwait(false);
            await Assert.That(bridge.VoiceSends.Contains((777, 555))).IsTrue();
            bridge.FireVoiceState(777, 555, "vsess");
            bridge.FireVoiceServer(777, "tok", "127.0.0.1");
            var conn = await connect.ConfigureAwait(false);
            await Assert.That(conn).IsNotNull();
            await Assert.That(conn!.GuildId).IsEqualTo(777);

            var body = await playerPatch
                .Task.WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(body).IsNotNull();
                await Assert.That(body).Contains("\"token\":\"tok\"");
                await Assert.That(body).Contains("\"sessionId\":\"vsess\"");
            }

            var track = JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.OptionsFor(true))!;
            await conn.PlayAsync(track, new PlayOptions { StartPosition = TimeSpan.FromSeconds(1) })
                .ConfigureAwait(false);
            var playBody = server
                .RestCalls.Where(c =>
                    c is { Method: "PATCH", Path: "/v4/sessions/fake-session/players/777" }
                )
                .Last()
                .Body;
            await Assert.That(playBody).IsNotNull();
            await Assert.That(playBody).Contains("\"track\":{\"encoded\":\"QXRsZWFzdA==\"}");
            await Assert.That(playBody).Contains("\"position\":1000");

            var started = new TaskCompletionSource();
            conn.TrackStarted += (_, _) =>
            {
                started.TrySetResult();
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    $$"""{"op":"event","guildId":"777","type":"TrackStartEvent","track":{{TrackJson}}}"""
                )
                .ConfigureAwait(false);
            await Assert.That(started.Task.Wait(TimeSpan.FromSeconds(5))).IsTrue();

            await conn.SetVolumeAsync(250).ConfigureAwait(false);
            var volumeBody = server
                .RestCalls.Where(c =>
                    c is { Method: "PATCH", Path: "/v4/sessions/fake-session/players/777" }
                )
                .Last()
                .Body;
            await Assert.That(volumeBody).IsEqualTo("""{"volume":250}""");

            await conn.PauseAsync().ConfigureAwait(false);
            var pauseBody = server
                .RestCalls.Where(c =>
                    c is { Method: "PATCH", Path: "/v4/sessions/fake-session/players/777" }
                )
                .Last()
                .Body;
            await Assert.That(pauseBody).IsEqualTo("""{"paused":true}""");

            await conn.SeekAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            var seekBody = server
                .RestCalls.Where(c =>
                    c is { Method: "PATCH", Path: "/v4/sessions/fake-session/players/777" }
                )
                .Last()
                .Body;
            await Assert.That(seekBody).IsEqualTo("""{"position":30000}""");

            var updateSeen = new TaskCompletionSource<long>();
            conn.PlayerUpdated += (_, args) =>
            {
                updateSeen.TrySetResult(args.State.PositionMilliseconds);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"playerUpdate","guildId":"777","state":{"time":1500,"position":60000,"connected":true,"ping":50}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    await updateSeen.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                )
                .IsEqualTo(60000);

            await conn.StopAsync().ConfigureAwait(false);
            var stopBody = server
                .RestCalls.Where(c =>
                    c is { Method: "PATCH", Path: "/v4/sessions/fake-session/players/777" }
                )
                .Last()
                .Body;
            await Assert.That(stopBody).IsNotNull();
            await Assert.That(stopBody).Contains("\"track\":{\"encoded\":null}");

            await conn.DisconnectAsync().ConfigureAwait(false);
            await Assert.That(bridge.VoiceSends.Contains((777, null))).IsTrue();
        }
    }

    private static string? LastPlayerBody(FakeNodeServer server)
    {
        foreach (var call in server.RestCalls.Reverse())
        {
            if (call is { Method: "PATCH", Path: "/v4/sessions/fake-session/players/777" })
                return call.Body;
        }
        return null;
    }

    [Test]
    public async Task GuildSurface()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            var track = JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.OptionsFor(true))!;

            await conn.SetEndTimeAsync(TimeSpan.FromSeconds(90)).ConfigureAwait(false);
            await Assert.That(LastPlayerBody(server)).IsEqualTo("""{"endTime":90000}""");

            await conn.Effects.SetNextTrackAsync(track).ConfigureAwait(false);
            await Assert
                .That(LastPlayerBody(server)?.Contains("\"nextTrack\":{\"encoded\""))
                .IsTrue();

            await conn
                .Effects.SetFadingAsync(
                    new FadingOptions
                    {
                        Enabled = true,
                        TrackStart = new FadeSection
                        {
                            DurationMilliseconds = 2000,
                            Curve = "linear",
                        },
                    }
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    LastPlayerBody(server) is { } fade
                        && fade.Contains("\"enabled\":true")
                        && fade.Contains("\"trackStart\"")
                )
                .IsTrue();

            await conn.Effects.SetLoudnessNormalizerAsync(true).ConfigureAwait(false);
            await Assert.That(LastPlayerBody(server)).IsEqualTo("""{"loudnessNormalizer":true}""");

            await conn.Effects.SetDuckingAsync(false).ConfigureAwait(false);
            await Assert.That(LastPlayerBody(server)).IsEqualTo("""{"ducking":false}""");

            await conn
                .Effects.SetCrossfadeAsync(
                    new CrossfadeOptions { Enabled = true, DurationMilliseconds = 3000 }
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    LastPlayerBody(server) is { } cross
                        && cross.Contains("\"crossfade\"")
                        && cross.Contains("\"duration\":3000")
                )
                .IsTrue();

            await conn
                .Filters.SetEqualizerAsync([new EqualizerBand(0, 0.5f)])
                .ConfigureAwait(false);
            await Assert
                .That(LastPlayerBody(server) is { } eq && eq.Contains("\"equalizer\""))
                .IsTrue();

            await conn
                .Filters.ApplyAsync(
                    new FilterChain
                    {
                        Equalizer = [new EqualizerBand(1, -0.25f)],
                        EqualizerTransition = new FilterTransition(500),
                        Karaoke = new KaraokeFilter { Level = 0.8f },
                    }
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    LastPlayerBody(server) is { } applied
                        && applied.Contains("\"bands\"")
                        && applied.Contains("\"transition\"")
                        && applied.Contains("\"karaoke\"")
                )
                .IsTrue();

            await conn.Filters.AddAsync(new TimescaleFilter { Speed = 1.2f }).ConfigureAwait(false);
            await Assert.That(LastPlayerBody(server)?.Contains("\"timescale\"")).IsTrue();

            await conn.Filters.RemoveAsync<KaraokeFilter>().ConfigureAwait(false);
            await Assert
                .That(
                    LastPlayerBody(server) is { } removed
                        && removed.Contains("\"timescale\"")
                        && removed.Contains("\"karaoke\":null")
                )
                .IsTrue();

            await conn.Filters.ClearAsync().ConfigureAwait(false);
            await Assert
                .That(LastPlayerBody(server) is { } cleared && cleared.Contains("\"filters\""))
                .IsTrue();

            var sponsorPath = "/v4/sessions/fake-session/players/777/sponsorblock";
            server.On(
                "GET",
                sponsorPath,
                _ => new FakeRestResponse(
                    200,
                    """{"enabled":true,"categories":["sponsor"],"actionTypes":["skip"]}"""
                )
            );
            var sponsor = await conn.SponsorBlock.GetAsync().ConfigureAwait(false);
            await Assert
                .That(sponsor is { Enabled: true, Categories: ["sponsor"], ActionTypes: ["skip"] })
                .IsTrue();

            server.On(
                "PATCH",
                sponsorPath,
                _ => new FakeRestResponse(200, """{"enabled":true,"categories":["sponsor"]}""")
            );
            var setSponsor = await conn
                .SponsorBlock.SetAsync(
                    new SponsorBlockSettings { Enabled = true, Categories = ["sponsor"] }
                )
                .ConfigureAwait(false);
            await Assert.That(setSponsor).IsNotNull();
            await Assert.That(setSponsor!.Enabled).IsTrue();

            await Assert
                .That(async () =>
                {
                    _ = await conn.SponsorBlock.SetAsync(null!).ConfigureAwait(false);
                })
                .Throws<ArgumentNullException>();

            await Assert
                .That(async () =>
                {
                    _ = await conn
                        .SponsorBlock.SetAsync(
                            new SponsorBlockSettings { SkipMarginMilliseconds = -1 }
                        )
                        .ConfigureAwait(false);
                })
                .Throws<ArgumentOutOfRangeException>();

            await Assert
                .That(async () =>
                {
                    _ = await conn
                        .SponsorBlock.SetAsync(
                            new SponsorBlockSettings
                            {
                                Categories =
                                    (IReadOnlyList<string>)new List<string?> { "sponsor", null },
                            }
                        )
                        .ConfigureAwait(false);
                })
                .Throws<ArgumentException>();

            await Assert
                .That(async () =>
                {
                    _ = await conn
                        .SponsorBlock.SetAsync(
                            new SponsorBlockSettings
                            {
                                ActionTypes =
                                    (IReadOnlyList<string>)new List<string?> { "skip", null },
                            }
                        )
                        .ConfigureAwait(false);
                })
                .Throws<ArgumentException>();

            server.On("DELETE", sponsorPath, _ => new FakeRestResponse(204, string.Empty));
            await conn.SponsorBlock.ClearAsync().ConfigureAwait(false);

            var subPath = "/v4/sessions/fake-session/players/777/lyrics/subscribe";
            var subscribed = new TaskCompletionSource<string?>();
            server.On(
                "POST",
                subPath,
                call =>
                {
                    subscribed.TrySetResult(call.Body);
                    return new FakeRestResponse(204, string.Empty);
                }
            );
            server.On("DELETE", subPath, _ => new FakeRestResponse(204, string.Empty));
            await conn.Lyrics.SubscribeAsync().ConfigureAwait(false);
            await Assert
                .That(
                    await subscribed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                )
                .IsNull();
            await conn.Lyrics.UnsubscribeAsync().ConfigureAwait(false);

            var mixPath = "/v4/sessions/fake-session/players/777/mix";
            server.On(
                "POST",
                mixPath,
                _ => new FakeRestResponse(
                    200,
                    $$"""{"id":"layer-1","track":{{TrackJson}},"volume":0.5}"""
                )
            );
            var layer = await conn.Mix.StartAsync(track, 0.5f).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(layer).IsNotNull();
                await Assert.That(layer!.Id).IsEqualTo("layer-1");
                await Assert.That(layer!.Volume).IsEqualTo(0.5f);
            }

            server.On(
                "GET",
                mixPath,
                _ => new FakeRestResponse(
                    200,
                    $$"""{"mixes":[{"id":"layer-1","track":{{TrackJson}},"volume":0.5}]}"""
                )
            );
            var layers = await conn.Mix.GetLayersAsync().ConfigureAwait(false);
            await Assert.That(layers).IsNotNull();
            await Assert.That(layers!.Count).IsEqualTo(1);

            server.On(
                "PATCH",
                "/v4/sessions/fake-session/players/777/mix/layer-1",
                _ => new FakeRestResponse(200, """{"id":"layer-1","volume":0.9}""")
            );
            await conn.Mix.SetVolumeAsync("layer-1", 0.9f).ConfigureAwait(false);

            server.On(
                "DELETE",
                "/v4/sessions/fake-session/players/777/mix/layer-1",
                _ => new FakeRestResponse(204, string.Empty)
            );
            await conn.Mix.StopAsync("layer-1").ConfigureAwait(false);

            server.On(
                "GET",
                "/v4/sessions/fake-session/players",
                _ => new FakeRestResponse(200, """[{"guildId":"777","volume":100}]""")
            );
            var players = await node.ListPlayersAsync().ConfigureAwait(false);
            await Assert.That(players).IsNotNull();
            await Assert.That(players!.Count).IsEqualTo(1);
            await Assert.That(players![0].GuildId).IsEqualTo(777);

            server.On(
                "GET",
                "/v4/trackstream",
                _ => new FakeRestResponse(
                    200,
                    """{"url":"https://stream/1","protocol":"https","format":"arbitrary"}"""
                )
            );
            var streamUrl = await node.Tracks.GetStreamUrlAsync(track).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(streamUrl).IsNotNull();
                await Assert.That(streamUrl!.Url).IsEqualTo("https://stream/1");
                await Assert.That(streamUrl!.Protocol).IsEqualTo("https");
            }
        }
    }

    [Test]
    public async Task LavalinkStack()
    {
        var stack = await StartAsync(
                new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" },
                new NodeOptions
                {
                    Name = "lava-fake",
                    Resuming = true,
                    ReconnectDelay = TimeSpan.FromMilliseconds(50),
                    MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                    VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                    ReadyTimeout = TimeSpan.FromSeconds(5),
                }
            )
            .ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            await Assert.That(node.IsNodeLink).IsFalse();

            server.On(
                "GET",
                "/v4/loadtracks",
                _ => new FakeRestResponse(200, $$"""{"loadType":"track","data":{{TrackJson}}}""")
            );
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            var track = JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.OptionsFor(false))!;

            server.On(
                "PATCH",
                "/v4/sessions/fake-session/players/777",
                call =>
                {
                    return new FakeRestResponse(
                        200,
                        $$$$"""{"guildId":"777","track":{{{{TrackJson}}}},"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42}}"""
                    );
                }
            );

            var identified = await conn.PlayIdentifierAsync("ytsearch:atwa").ConfigureAwait(false);
            await Assert
                .That(LastPlayerBody(server)?.Contains("\"identifier\":\"ytsearch:atwa\""))
                .IsTrue();

            await conn.PlayAsync(track).ConfigureAwait(false);
            await Assert
                .That(LastPlayerBody(server)?.Contains("\"encoded\":\"QXRsZWFzdA==\""))
                .IsTrue();

            await Assert
                .That(async () =>
                {
                    await conn.Filters.SetVolumeFilterAsync(6f).ConfigureAwait(false);
                })
                .Throws<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task PushedEvents()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            var finished = new TaskCompletionSource<string?>();
            conn.TrackFinished += (_, args) =>
            {
                finished.TrySetResult(args.EndReason.ToString());
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    $$"""{"op":"event","guildId":"777","type":"TrackEndEvent","track":{{TrackJson}},"reason":"finished"}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await finished.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsEqualTo(TrackEndReason.Finished.ToString());

            var stuck = new TaskCompletionSource<long?>();
            conn.TrackStuck += (_, args) =>
            {
                stuck.TrySetResult(args.ThresholdMilliseconds);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    $$"""{"op":"event","guildId":"777","type":"TrackStuckEvent","track":{{TrackJson}},"thresholdMs":300}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await stuck.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsEqualTo(300);

            var errored = new TaskCompletionSource<string?>();
            conn.TrackException += (_, args) =>
            {
                errored.TrySetResult(args.Exception?.Message);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"TrackExceptionEvent","exception":{"message":"the source died","severity":"suspicious"}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await errored.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsEqualTo("the source died");

            var closed = new TaskCompletionSource<int>();
            conn.WebSocketClosed += (_, args) =>
            {
                closed.TrySetResult(args.Code ?? -1);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"WebSocketClosedEvent","code":4014,"byRemote":true,"reason":"rate limited"}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsEqualTo(4014);

            var lyricsLine = new TaskCompletionSource<bool>();
            conn.LyricsLine += (_, args) =>
            {
                lyricsLine.TrySetResult(args.Line is not null);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"LyricsLineEvent","line":{"text":"line one","time":100,"duration":200}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    await lyricsLine.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                )
                .IsTrue();

            var segments = new TaskCompletionSource<int>();
            conn.SponsorBlockSegmentsLoaded += (_, args) =>
            {
                segments.TrySetResult(args.Segments?.Count ?? -1);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"SponsorBlockSegmentsLoadedEvent","segments":[{"segment":"sponsor","startAt":10,"endAt":20,"skipped":false}]}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await segments.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsEqualTo(1);

            var mixStarted = new TaskCompletionSource<bool>();
            conn.MixStarted += (_, _) =>
            {
                mixStarted.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    $$$$"""{"op":"event","guildId":"777","type":"MixStartedEvent","mix":{"id":"layer-2","track":{{{{TrackJson}}}},"volume":0.5}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    await mixStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task Guards()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            var track = JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.OptionsFor(true))!;

            var rejected = 0;
            async Task ExpectThrowAsync(Func<Task> action)
            {
                await Assert
                    .That(async () => await action().ConfigureAwait(false))
                    .Throws<ArgumentException>();
                rejected++;
            }

            await ExpectThrowAsync(() =>
                    conn.PlayAsync(
                        track,
                        new PlayOptions { StartPosition = TimeSpan.FromSeconds(-5) }
                    )
                )
                .ConfigureAwait(false);
            await ExpectThrowAsync(() =>
                    conn.PlayAsync(track, new PlayOptions { EndTime = TimeSpan.FromSeconds(-1) })
                )
                .ConfigureAwait(false);
            await ExpectThrowAsync(() => conn.SetVolumeAsync(-1)).ConfigureAwait(false);
            await ExpectThrowAsync(() => conn.SetVolumeAsync(1001)).ConfigureAwait(false);
            await ExpectThrowAsync(() => conn.SeekAsync(TimeSpan.FromSeconds(-1)))
                .ConfigureAwait(false);
            await ExpectThrowAsync(() =>
                    conn.Filters.ApplyAsync(
                        new FilterChain { Compressor = new CompressorFilter(Ratio: 0.5f) }
                    )
                )
                .ConfigureAwait(false);
            await ExpectThrowAsync(() =>
                    conn.Filters.ApplyAsync(new FilterChain { Chorus = new ChorusFilter(Rate: 0f) })
                )
                .ConfigureAwait(false);
            await ExpectThrowAsync(() =>
                    conn.Filters.ApplyAsync(
                        new FilterChain { Reverb = new ReverbFilter(RoomSize: float.NaN) }
                    )
                )
                .ConfigureAwait(false);
            await ExpectThrowAsync(() =>
                    conn.PlayAsync(
                        JsonSerializer.Deserialize<Track>(
                            """{"encoded":"","info":{"identifier":"x","author":"a","title":"t","length":1,"isStream":false,"isSeekable":true,"position":0}}""",
                            NyraJson.OptionsFor(true)
                        )!
                    )
                )
                .ConfigureAwait(false);

            Track? nullTrack = null;
            await ExpectThrowAsync(() => conn.PlayAsync(nullTrack!)).ConfigureAwait(false);
            await ExpectThrowAsync(() => conn.SetEndTimeAsync(TimeSpan.Zero)).ConfigureAwait(false);

            await Assert.That(rejected).IsEqualTo(11);
        }
    }

    [Test]
    public async Task FilterBounds()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            var cases = new FilterChain[]
            {
                new() { Karaoke = new KaraokeFilter { Level = 1.5f } },
                new() { Karaoke = new KaraokeFilter { MonoLevel = -0.1f } },
                new() { Karaoke = new KaraokeFilter { FilterBand = -1 } },
                new() { Karaoke = new KaraokeFilter { FilterWidth = -1 } },
                new() { Tremolo = new TremoloFilter { Frequency = 0 } },
                new() { Vibrato = new VibratoFilter { Frequency = 0 } },
                new() { Vibrato = new VibratoFilter { Depth = 2.5f } },
                new() { Echo = new EchoFilter { DelayMilliseconds = 0 } },
                new() { Echo = new EchoFilter { Feedback = 1.5f } },
                new() { Echo = new EchoFilter { Mix = 1.5f } },
                new() { Reverb = new ReverbFilter { RoomSize = 1.5f } },
                new() { Reverb = new ReverbFilter { Damping = -0.1f } },
                new() { Reverb = new ReverbFilter { Mix = 2f } },
                new() { Phaser = new PhaserFilter { Depth = 0f } },
                new() { Phaser = new PhaserFilter { Mix = 0f } },
                new() { Flanger = new FlangerFilter { Depth = 0f } },
                new() { Chorus = new ChorusFilter { Depth = 0f } },
                new() { Chorus = new ChorusFilter { Mix = 0f } },
                new() { Compressor = new CompressorFilter(Attack: -0.1f) },
                new() { Compressor = new CompressorFilter(Release: -0.1f) },
                new() { Phonograph = new PhonographFilter(Crackle: 1.5f) },
                new() { Phonograph = new PhonographFilter(Depth: -1f) },
                new() { Spatial = new SpatialFilter(Depth: 1.5f) },
            };

            await Assert
                .That(() =>
                {
                    _ = new EqualizerBand(15, 0.5f);
                })
                .Throws<ArgumentOutOfRangeException>();
            await Assert
                .That(() =>
                {
                    _ = new EqualizerBand(0, 6f);
                })
                .Throws<ArgumentOutOfRangeException>();

            foreach (var chain in cases)
                await Assert
                    .That(async () => await conn.Filters.ApplyAsync(chain).ConfigureAwait(false))
                    .Throws<ArgumentException>();

            var transition = new FilterTransition(400, "linear");
            var positives = new (FilterChain Chain, string Key)[]
            {
                (
                    new()
                    {
                        Karaoke = new KaraokeFilter { Level = 0.8f, Transition = transition },
                    },
                    "karaoke"
                ),
                (
                    new()
                    {
                        Timescale = new TimescaleFilter { Speed = 1.2f, Transition = transition },
                    },
                    "timescale"
                ),
                (
                    new()
                    {
                        Tremolo = new TremoloFilter { Frequency = 8, Transition = transition },
                    },
                    "tremolo"
                ),
                (
                    new()
                    {
                        Vibrato = new VibratoFilter { Frequency = 10, Transition = transition },
                    },
                    "vibrato"
                ),
                (
                    new()
                    {
                        Distortion = new DistortionFilter { SinScale = 2, Transition = transition },
                    },
                    "distortion"
                ),
                (
                    new()
                    {
                        Rotation = new RotationFilter { RotationHz = 2, Transition = transition },
                    },
                    "rotation"
                ),
                (
                    new()
                    {
                        ChannelMix = new ChannelMixFilter
                        {
                            LeftToLeft = 0.9f,
                            Transition = transition,
                        },
                    },
                    "channelMix"
                ),
                (
                    new()
                    {
                        LowPass = new LowPassFilter { Smoothing = 2, Transition = transition },
                    },
                    "lowPass"
                ),
                (
                    new()
                    {
                        Echo = new EchoFilter
                        {
                            DelayMilliseconds = 300,
                            Mix = 0.4f,
                            Transition = transition,
                        },
                    },
                    "echo"
                ),
                (
                    new()
                    {
                        Reverb = new ReverbFilter { RoomSize = 0.7f, Transition = transition },
                    },
                    "reverb"
                ),
                (
                    new() { HighPass = new HighPassFilter(Smoothing: 3, Transition: transition) },
                    "highpass"
                ),
                (
                    new()
                    {
                        Chorus = new ChorusFilter(
                            Rate: 1.5f,
                            Depth: 0.7f,
                            Mix: 0.5f,
                            Transition: transition
                        ),
                    },
                    "chorus"
                ),
                (
                    new()
                    {
                        Compressor = new CompressorFilter(Threshold: -30f, Transition: transition),
                    },
                    "compressor"
                ),
                (
                    new()
                    {
                        Flanger = new FlangerFilter(
                            Rate: 0.5f,
                            Depth: 0.8f,
                            Transition: transition
                        ),
                    },
                    "flanger"
                ),
                (
                    new()
                    {
                        Phaser = new PhaserFilter(Rate: 2f, Depth: 0.6f, Transition: transition),
                    },
                    "phaser"
                ),
                (
                    new()
                    {
                        Phonograph = new PhonographFilter(Crackle: 0.3f, Transition: transition),
                    },
                    "phonograph"
                ),
                (new() { Spatial = new SpatialFilter(X: 0.5f, Transition: transition) }, "spatial"),
            };

            var applied = 0;
            foreach (var (chain, key) in positives)
            {
                await conn.Filters.ApplyAsync(chain).ConfigureAwait(false);
                var body = LastPlayerBody(server);
                var landed =
                    body is not null
                    && body.Contains($"\"{key}\":")
                    && body.Contains("\"durationMs\":400");
                applied += landed ? 1 : 0;
                await Assert.That(landed).IsTrue();
            }
            await Assert.That(applied).IsEqualTo(positives.Length);

            await conn
                .Filters.ApplyAsync(
                    new FilterChain
                    {
                        Equalizer = [new EqualizerBand(0, 0.5f)],
                        EqualizerTransition = transition,
                    }
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    LastPlayerBody(server) is { } eq
                        && eq.Contains("\"bands\"")
                        && eq.Contains("\"durationMs\":400")
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task RemainingEvents()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            var created = new TaskCompletionSource<bool>();
            conn.PlayerCreated += (_, _) =>
            {
                created.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"PlayerCreatedEvent","player":{"guildId":"777","volume":100}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await created.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsTrue();

            var destroyed = new TaskCompletionSource<bool>();
            conn.PlayerDestroyed += (_, _) =>
            {
                destroyed.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync("""{"op":"event","guildId":"777","type":"PlayerDestroyedEvent"}""")
                .ConfigureAwait(false);
            await Assert
                .That(await destroyed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsTrue();

            var reconnecting = new TaskCompletionSource<bool>();
            conn.PlayerReconnecting += (_, _) =>
            {
                reconnecting.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"PlayerReconnectingEvent","voice":{"token":"t","endpoint":"e","sessionId":"s"}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    await reconnecting.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                )
                .IsTrue();

            var mixEnded = new TaskCompletionSource<bool>();
            conn.MixEnded += (_, _) =>
            {
                mixEnded.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"MixEndedEvent","mix":{"id":"layer-1"}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await mixEnded.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsTrue();

            var skipped = new TaskCompletionSource<bool>();
            conn.SponsorBlockSegmentSkipped += (_, _) =>
            {
                skipped.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    $$$$"""{"op":"event","guildId":"777","type":"SponsorBlockSegmentSkippedEvent","track":{{{{TrackJson}}}},"segment":{"uuid":"u1","start":10,"end":20,"category":"sponsor"}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await skipped.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsTrue();

            var lyricsNotFound = new TaskCompletionSource<bool>();
            conn.LyricsNotFound += (_, _) =>
            {
                lyricsNotFound.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync("""{"op":"event","guildId":"777","type":"LyricsNotFoundEvent"}""")
                .ConfigureAwait(false);
            await Assert
                .That(
                    await lyricsNotFound
                        .Task.WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false)
                )
                .IsTrue();

            var boxJumped = new TaskCompletionSource<bool>();
            conn.EternalBoxJumped += (_, _) =>
            {
                boxJumped.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","guildId":"777","type":"EternalBoxJumpEvent","from":1,"to":2}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await boxJumped.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsTrue();

            var streamMeta = new TaskCompletionSource<bool>();
            conn.StreamMetadataUpdated += (_, _) =>
            {
                streamMeta.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    $$$$"""{"op":"event","guildId":"777","type":"StreamMetadataEvent","track":{{{{TrackJson}}}},"stream":{"metadata":{"streamTitle":"live"}}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    await streamMeta.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                )
                .IsTrue();

            var workerFired = new TaskCompletionSource<bool>();
            stack.Extension.NodeWorkerFailed += (_, _) =>
            {
                workerFired.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"event","type":"WorkerFailedEvent","workerId":3,"affectedGuilds":["888"]}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    await workerFired.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task EchoState()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);

            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            server.On(
                "PATCH",
                "/v4/sessions/fake-session/players/777",
                _ => new FakeRestResponse(
                    200,
                    """
                    {"guildId":"777","track":null,"volume":250,"paused":true,
                     "filters":{"volume":1.4,
                       "equalizer":{"bands":[{"band":0,"gain":0.5}],"transition":{"durationMs":400}},
                       "karaoke":{"level":0.75},"timescale":{"speed":1.25},"tremolo":{"frequency":8},
                       "vibrato":{"frequency":10},"distortion":{"sinScale":2},"rotation":{"rotationHz":2},
                       "channelMix":{"leftToLeft":0.9},"lowPass":{"smoothing":3},"lowpass":{"smoothing":4},
                       "echo":{"delay":300,"mix":0.4},"reverb":{"roomSize":0.7},
                       "highpass":{"smoothing":5},"chorus":{"rate":1.5,"depth":0.7,"mix":0.5},
                       "compressor":{"threshold":-30,"ratio":6},"flanger":{"rate":0.5,"depth":0.8},
                       "phaser":{"rate":2,"depth":0.6},"phonograph":{"crackle":0.3},
                       "spatial":{"x":0.5,"y":-0.5},"pluginFilters":{"custom":{"x":1}}},
                     "voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"},
                     "state":{"time":1,"position":2,"connected":true,"ping":3}}
                    """
                )
            );

            await conn.Filters.ApplyAsync(new FilterChain { Volume = null }).ConfigureAwait(false);
            await conn.SetVolumeAsync(250).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(conn.Filters.Applied).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Karaoke).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Karaoke!.Level).IsEqualTo(0.75f);
                await Assert.That(conn.Filters.Applied!.Timescale).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Timescale!.Speed).IsEqualTo(1.25f);
                await Assert.That(conn.Filters.Applied).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Chorus).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Chorus!.Rate).IsEqualTo(1.5f);
                await Assert.That(conn.Filters.Applied!.Compressor).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Compressor!.Ratio).IsEqualTo(6f);
                await Assert.That(conn.Filters.Applied!.Flanger).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Flanger!.Depth).IsEqualTo(0.8f);
                await Assert.That(conn.Filters.Applied!.Phaser).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Phaser!.Rate).IsEqualTo(2f);
                await Assert.That(conn.Filters.Applied!.Phonograph).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Phonograph!.Crackle).IsEqualTo(0.3f);
                await Assert.That(conn.Filters.Applied!.Spatial).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Spatial!.X).IsEqualTo(0.5f);
                await Assert.That(conn.Filters.Applied!.Echo).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Echo!.DelayMilliseconds).IsEqualTo(300);
                await Assert.That(conn.Filters.Applied!.Reverb).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Reverb!.RoomSize).IsEqualTo(0.7f);
                await Assert.That(conn.Filters.Applied!.HighPass).IsNotNull();
                await Assert.That(conn.Filters.Applied!.HighPass!.Smoothing).IsEqualTo(5);
                await Assert.That(conn.Filters.Applied).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Equalizer).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Equalizer!.Count).IsEqualTo(1);
                await Assert.That(conn.Filters.Applied!.EqualizerTransition).IsNotNull();
                await Assert
                    .That(conn.Filters.Applied!.EqualizerTransition!.DurationMilliseconds)
                    .IsEqualTo(400);
                await Assert.That(conn.CurrentTrack).IsNull();
                await Assert.That(conn.Volume).IsEqualTo(250);
                await Assert.That(conn.IsPaused).IsTrue();
            }

            server.On(
                "PATCH",
                "/v4/sessions/fake-session/players/777",
                _ => new FakeRestResponse(
                    200,
                    """{"guildId":"777","filters":{"chorus":{"_disabled":true},"karaoke":{"level":0.5}},"volume":100}"""
                )
            );
            await conn.Filters.ApplyAsync(new FilterChain { Volume = null }).ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(conn.Filters.Applied).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Chorus).IsNull();
                await Assert.That(conn.Filters.Applied!.Karaoke).IsNotNull();
                await Assert.That(conn.Filters.Applied!.Karaoke!.Level).IsEqualTo(0.5f);
            }
        }
    }

    [Test]
    public async Task EncodeRoundTrip()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;
            var server = stack.Server;

            server.On(
                "POST",
                "/v4/encodedtracks",
                _ => new FakeRestResponse(200, """["QXRsZWFzdA=="]""")
            );
            var info = new TrackInfo
            {
                Identifier = "ph8qt3dhvwo",
                Author = "SoAD - Topic",
                Title = "ATWA",
            };
            var encoded = await node.Tracks.EncodeManyAsync([info]).ConfigureAwait(false);
            await Assert.That(encoded).IsNotNull();
            await Assert.That(encoded!.Count).IsEqualTo(1);
            await Assert.That(encoded![0]).IsEqualTo("QXRsZWFzdA==");
            var encodeCall = server.RestCalls.FirstOrDefault(c => c.Path == "/v4/encodedtracks");
            await Assert
                .That(
                    encodeCall?.Body is { } body
                        && body.Contains(""""identifier":"ph8qt3dhvwo"""")
                        && body.Contains(""""title":"ATWA"""")
                )
                .IsTrue();

            server.On("GET", "/v4/decodetrack", _ => new FakeRestResponse(200, TrackJson));
            var decoded = await node.Tracks.DecodeAsync("QXRsZWFzdA==").ConfigureAwait(false);
            await Assert.That(decoded.Info?.Title).IsEqualTo("ATWA");
        }
    }

    [Test]
    public async Task Endpoint()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var plain = new Endpoint("127.0.0.1", 3000, "youshallnotpass");
        await Assert.That(plain.ToWebSocketUri().Scheme).IsEqualTo("ws");
        await Assert.That(plain.ToRestUri().Scheme).IsEqualTo("http");

        var secure = new Endpoint("example.com", 443, "secret", isSecure: true);
        using (Assert.Multiple())
        {
            await Assert.That(secure).IsNotNull();
            await Assert.That(secure!.IsSecure).IsTrue();
            await Assert.That(secure!.ToWebSocketUri().Scheme).IsEqualTo("wss");
            await Assert.That(secure.ToRestUri().Scheme).IsEqualTo("https");
        }

        var ipv6 = new Endpoint("[::1]", 3000, "secret");
        await Assert.That(ipv6.ToWebSocketUri().ToString()).StartsWith("ws://[::1]:3000");
    }

    [Test]
    [Arguments("")]
    [Arguments("  ")]
    [Arguments("host name")]
    [Arguments("host\tname")]
    public async Task EndpointRejectsBlankAndSpacedHosts(string host)
    {
        await Assert
            .That(() =>
            {
                _ = new Endpoint(host, 3000, "secret");
            })
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(-1)]
    [Arguments(0)]
    [Arguments(65536)]
    public async Task EndpointRejectsOutOfRangePorts(int port)
    {
        await Assert
            .That(() =>
            {
                _ = new Endpoint("127.0.0.1", port, "secret");
            })
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task RestErrors()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;
            var server = stack.Server;

            server.On(
                "GET",
                "/v4/stats",
                _ => new FakeRestResponse(400, """{"message":"bad request"}""")
            );
            var badRequest = await Assert
                .That(async () => await node.GetStatsAsync().ConfigureAwait(false))
                .Throws<RestException>()
                .WithMessageContaining("bad request");
            await Assert.That(badRequest!.StatusCode).IsEqualTo(400);

            server.On(
                "GET",
                "/v4/stats",
                _ => new FakeRestResponse(429, """{"message":"slow down"}""")
            );
            var rateLimited = await Assert
                .That(async () => await node.GetStatsAsync().ConfigureAwait(false))
                .Throws<RestException>();
            await Assert.That(rateLimited!.StatusCode).IsEqualTo(429);

            server.On("GET", "/v4/stats", _ => new FakeRestResponse(500, string.Empty));
            var emptyBody = await Assert
                .That(async () => await node.GetStatsAsync().ConfigureAwait(false))
                .Throws<RestException>();
            await Assert.That(emptyBody!.StatusCode).IsEqualTo(500);
            await Assert.That(emptyBody.Message).IsNotEmpty();

            server.On("GET", "/v4/stats", _ => new FakeRestResponse(200, "not json at all"));
            await Assert
                .That(async () => await node.GetStatsAsync().ConfigureAwait(false))
                .Throws<RestException>();

            server.On("GET", "/v4/loadstream", _ => new FakeRestResponse(200, "audio-bytes-here"));
            var track = JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.OptionsFor(true))!;
            var audio = await node.Tracks.OpenAudioStreamAsync(track).ConfigureAwait(false);
            await using (audio.ConfigureAwait(false))
            {
                var buffer = new byte[64];
                var read = await audio.ReadAsync(buffer).ConfigureAwait(false);
                await Assert
                    .That(System.Text.Encoding.UTF8.GetString(buffer, 0, read))
                    .Contains("audio-bytes");
            }

            server.On("GET", "/v4/trackstream", _ => new FakeRestResponse(200, "track-bytes"));
            var trackAudio = await node.Tracks.OpenAudioStreamAsync(track).ConfigureAwait(false);
            await using (trackAudio.ConfigureAwait(false))
            {
                var buffer = new byte[64];
                var read = await trackAudio.ReadAsync(buffer).ConfigureAwait(false);
                await Assert.That(read).IsGreaterThan(0);
            }
        }
    }

    [Test]
    public async Task HostileFrames()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            var unexpected = 0;
            stack.Extension.NodeError += (_, _) =>
            {
                unexpected++;
                return Task.CompletedTask;
            };

            await server.PushAsync("""{"op":"brand-new"}""").ConfigureAwait(false);
            await server
                .PushAsync("""{"op":"event","type":"NeverSeenBeforeEvent"}""")
                .ConfigureAwait(false);
            await server.PushAsync("just a string").ConfigureAwait(false);
            await server.PushAsync("42").ConfigureAwait(false);
            await server
                .PushAsync(
                    """{"op":"event","guildId":"not-a-snowflake","type":"TrackStartEvent"}"""
                )
                .ConfigureAwait(false);
            await server
                .PushAsync("""{"op":"stats","players":"not-a-number"}""")
                .ConfigureAwait(false);

            await Task.Delay(300).ConfigureAwait(false);
            await Assert.That(node.IsConnected).IsTrue();

            var stillDelivers = new TaskCompletionSource<bool>();
            conn.PlayerUpdated += (_, _) =>
            {
                stillDelivers.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"playerUpdate","guildId":"777","state":{"time":1,"position":2,"connected":true,"ping":3}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(
                    await stillDelivers
                        .Task.WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false)
                )
                .IsTrue();

            var status = new TaskCompletionSource<bool>();
            stack.Extension.NodeConnectionStatusUpdated += (_, _) =>
            {
                status.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync("""{"op":"event","type":"ConnectionStatusEvent","status":"good"}""")
                .ConfigureAwait(false);
            await Assert
                .That(await status.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsTrue();
        }
    }

    [Test]
    public async Task VoiceTimeout()
    {
        var stack = await StartAsync(
                options: new NodeOptions
                {
                    Name = "timeout",
                    Resuming = false,
                    ReconnectDelay = TimeSpan.FromMilliseconds(50),
                    MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                    VoiceHandshakeTimeout = TimeSpan.FromSeconds(2),
                    ReadyTimeout = TimeSpan.FromSeconds(5),
                }
            )
            .ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);

            server.On(
                "PATCH",
                "/v4/sessions/fake-session/players/555",
                _ => new FakeRestResponse(200, """{"guildId":"555","volume":100}""")
            );

            var connect = node.ConnectAsync(555, 556);
            await Task.Delay(100).ConfigureAwait(false);
            await Assert
                .That(async () => await connect.ConfigureAwait(false))
                .Throws<VoiceHandshakeException>();
            await Assert
                .That(bridge.VoiceSends.Count(v => v.ChannelId is null))
                .IsGreaterThanOrEqualTo(1);

            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await Assert.That(conn.GuildId).IsEqualTo(777);
        }
    }

    [Test]
    public async Task ReconnectStorm()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (extension, node, server) = (stack.Extension, stack.Node, stack.Server);

            var drops = 0;
            extension.NodeDisconnected += (_, _) =>
            {
                drops++;
                return Task.CompletedTask;
            };

            for (var i = 0; i < 3; i++)
            {
                var noticed = new TaskCompletionSource();
                AsyncEventHandler<NodeDisconnectedEventArgs> saw = (_, _) =>
                {
                    noticed.TrySetResult();
                    return Task.CompletedTask;
                };
                extension.NodeDisconnected += saw;
                await server.DropConnectionAsync(1000, $"restart {i}").ConfigureAwait(false);
                var sawIt =
                    noticed.Task.IsCompleted
                    || await Task.WhenAny(noticed.Task, Task.Delay(TimeSpan.FromSeconds(10)))
                        .ConfigureAwait(false) == noticed.Task;
                extension.NodeDisconnected -= saw;
                await Assert.That(sawIt).IsTrue();

                await Assert
                    .That(() => node.IsConnected && node.IsReady)
                    .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(10));
            }
            await Assert.That(drops).IsGreaterThanOrEqualTo(3);
            await Assert.That(server.WebsocketConnections).IsGreaterThanOrEqualTo(4);
            await Assert.That(server.LastResumeFlag).IsTrue();
        }
    }

    private static void WriteU16(List<byte> buffer, ushort value)
    {
        buffer.Add((byte)(value >> 8));
        buffer.Add((byte)(value & 0xff));
    }

    private static void WriteLong(List<byte> buffer, long value)
    {
        for (var i = 7; i >= 0; i--)
            buffer.Add((byte)((value >> (8 * i)) & 0xff));
    }

    private static void WriteJavaUtf(List<byte> buffer, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        WriteU16(buffer, (ushort)bytes.Length);
        buffer.AddRange(bytes);
    }

    private static void WriteNullableJavaUtf(List<byte> buffer, string? value)
    {
        if (value is null)
        {
            buffer.Add(0);
            return;
        }
        buffer.Add(1);
        WriteJavaUtf(buffer, value);
    }

    private static string EncodeTrackMessage(byte[] message, bool versioned)
    {
        var header = ((versioned ? 1 : 0) << 30) | message.Length;
        var raw = new byte[4 + message.Length];
        raw[0] = (byte)(header >> 24);
        raw[1] = (byte)((header >> 16) & 0xff);
        raw[2] = (byte)((header >> 8) & 0xff);
        raw[3] = (byte)(header & 0xff);
        message.CopyTo(raw, 4);
        return Convert.ToBase64String(raw);
    }

    private static byte[] BuildMessage(
        byte? version,
        string title,
        string author,
        long length,
        string identifier,
        bool isStream,
        string? uri,
        string? artwork,
        string? isrc,
        string source,
        long position,
        byte[]? trailer = null
    )
    {
        var message = new List<byte>();
        if (version is { } stamped)
            message.Add(stamped);
        WriteJavaUtf(message, title);
        WriteJavaUtf(message, author);
        WriteLong(message, length);
        WriteJavaUtf(message, identifier);
        message.Add((byte)(isStream ? 1 : 0));
        if (version is >= 2)
            WriteNullableJavaUtf(message, uri);
        if (version is >= 3)
        {
            WriteNullableJavaUtf(message, artwork);
            WriteNullableJavaUtf(message, isrc);
        }
        WriteJavaUtf(message, source);
        WriteLong(message, position);
        if (trailer is not null)
            message.AddRange(trailer);
        return [.. message];
    }

    private static byte[] LegacyTrailer(bool seekable)
    {
        var trailer = new List<byte> { 1, 0, 13 };
        trailer.AddRange("NLK:seekable"u8.ToArray());
        trailer.Add((byte)(seekable ? 'Y' : 'N'));
        return [.. trailer];
    }

    [Test]
    public async Task TrackCodecs()
    {
        await Task.CompletedTask.ConfigureAwait(false);

        var v1 = EncodeTrackMessage(
            BuildMessage(
                version: null,
                title: "ATWA",
                author: "SoAD",
                length: 176000,
                identifier: "ph8qt3dhvwo",
                isStream: false,
                uri: null,
                artwork: null,
                isrc: null,
                source: "youtube",
                position: 0
            ),
            versioned: false
        );
        var v1Track = Track.Decode(v1);
        using (Assert.Multiple())
        {
            await Assert.That(v1Track.Info).IsNotNull();
            await Assert.That(v1Track.Info!.Title).IsEqualTo("ATWA");
            await Assert.That(v1Track.Info!.Author).IsEqualTo("SoAD");
            await Assert.That(v1Track.Info!.LengthMilliseconds).IsEqualTo(176000);
            await Assert.That(v1Track.Info!.Identifier).IsEqualTo("ph8qt3dhvwo");
            await Assert.That(v1Track.Info!.IsSeekable).IsTrue();
            await Assert.That(v1Track.Info!.Uri).IsNull();
            await Assert.That(v1Track.Info!.SourceName).IsEqualTo("youtube");
        }

        var v2 = EncodeTrackMessage(
            BuildMessage(
                version: 2,
                title: "Live",
                author: "Artist",
                length: 0,
                identifier: "stream1",
                isStream: true,
                uri: "https://example.com/live",
                artwork: null,
                isrc: null,
                source: "radio",
                position: 0
            ),
            versioned: true
        );
        var v2Track = Track.Decode(v2);
        using (Assert.Multiple())
        {
            await Assert.That(v2Track.Info).IsNotNull();
            await Assert.That(v2Track.Info!.IsStream).IsTrue();
            await Assert.That(v2Track.Info!.IsSeekable).IsFalse();
            await Assert.That(v2Track.Info!.Uri).IsEqualTo("https://example.com/live");
        }

        var v3 = EncodeTrackMessage(
            BuildMessage(
                version: 3,
                title: "T",
                author: "A",
                length: 5,
                identifier: "i",
                isStream: false,
                uri: "https://u",
                artwork: "https://art",
                isrc: "AAA000000001",
                source: "spotify",
                position: 1234
            ),
            versioned: true
        );
        var v3Track = Track.Decode(v3);
        using (Assert.Multiple())
        {
            await Assert.That(v3Track.Info).IsNotNull();
            await Assert.That(v3Track.Info!.ArtworkUrl).IsEqualTo("https://art");
            await Assert.That(v3Track.Info!.Isrc).IsEqualTo("AAA000000001");
            await Assert.That(v3Track.Info!.PositionMilliseconds).IsEqualTo(1234);
        }

        var legacy = EncodeTrackMessage(
            BuildMessage(
                version: null,
                title: "Old",
                author: "NodeLink",
                length: 1,
                identifier: "old",
                isStream: false,
                uri: null,
                artwork: null,
                isrc: null,
                source: "youtube",
                position: 7,
                trailer: LegacyTrailer(seekable: true)
            ),
            versioned: false
        );
        var legacyTrack = Track.Decode(legacy);
        using (Assert.Multiple())
        {
            await Assert.That(legacyTrack.Info).IsNotNull();
            await Assert.That(legacyTrack.Info!.Title).IsEqualTo("Old");
            await Assert.That(legacyTrack.Info!.PositionMilliseconds).IsEqualTo(7);
        }

        await Assert
            .That(() =>
            {
                _ = Track.Decode("not base64!!");
            })
            .Throws<InvalidTrackException>();

        var badHeader = Convert.ToBase64String(new byte[] { 0x40, 0, 0, 5, 1 });
        await Assert
            .That(() =>
            {
                _ = Track.Decode(badHeader);
            })
            .Throws<InvalidTrackException>();

        var unsupported = EncodeTrackMessage(
            BuildMessage(
                version: 99,
                title: "x",
                author: "y",
                length: 1,
                identifier: "z",
                isStream: false,
                uri: null,
                artwork: null,
                isrc: null,
                source: "s",
                position: 0
            ),
            versioned: true
        );
        await Assert
            .That(() =>
            {
                _ = Track.Decode(unsupported);
            })
            .Throws<InvalidTrackException>();

        var truncated = EncodeTrackMessage(
            BuildMessage(
                version: null,
                title: "x",
                author: "y",
                length: 1,
                identifier: "z",
                isStream: false,
                uri: null,
                artwork: null,
                isrc: null,
                source: "s",
                position: 0
            )[0..10],
            versioned: false
        );
        await Assert
            .That(() =>
            {
                _ = Track.Decode(truncated);
            })
            .Throws<InvalidTrackException>();

        await Assert
            .That(() =>
            {
                _ = Track.Decode("   ");
            })
            .Throws<InvalidTrackException>();
    }

    [Test]
    public async Task NodeLifecycle()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, extension, node, server) = (
                stack.Bridge,
                stack.Extension,
                stack.Node,
                stack.Server
            );

            extension.AddNode(node);
            await Assert.That(extension.Nodes.Count).IsEqualTo(1);

            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);
            await conn
                .Filters.ApplyAsync(
                    new FilterChain { EqualizerTransition = new FilterTransition(300, "linear") }
                )
                .ConfigureAwait(false);
            var tombstone = LastPlayerBody(server);
            using (Assert.Multiple())
            {
                await Assert.That(tombstone).IsNotNull();
                await Assert.That(tombstone).Contains("\u0022bands\u0022:[]");
                await Assert.That(tombstone).Contains("\u0022durationMs\u0022:300");
            }

            var survived = new TaskCompletionSource<bool>();
            conn.PlayerUpdated += (_, _) =>
            {
                survived.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"playerUpdate","guildId":"777","state":{"time":1,"position":2,"connected":true,"ping":3}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await survived.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsTrue();

            conn.TrackStarted += (_, _) => throw new InvalidOperationException("the handler broke");
            await server
                .PushAsync(
                    $$"""{"op":"event","guildId":"777","type":"TrackStartEvent","track":{{TrackJson}}}"""
                )
                .ConfigureAwait(false);
            await Task.Delay(300).ConfigureAwait(false);
            await Assert.That(node.IsConnected).IsTrue();

            await node.DisconnectAsync().ConfigureAwait(false);
            await Assert.That(node.IsConnected).IsFalse();

            var emptyBridge = new FakeBridge();
            emptyBridge.GoReady();
            var empty = new NyraExtension(emptyBridge);
            await using (empty.ConfigureAwait(false))
            {
                await empty.ConnectAllNodesAsync().ConfigureAwait(false);
                await Assert.That(empty.Nodes.Count).IsEqualTo(0);

                empty.Dispose();
                await Assert
                    .That(() =>
                    {
                        empty.AddNode(
                            new Node(
                                new Endpoint("127.0.0.1", 1, "x"),
                                new NodeOptions { Name = "late" }
                            )
                        );
                    })
                    .Throws<ObjectDisposedException>();
            }
        }
    }

    [Test]
    public async Task GuildTakeover()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);

            var first = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            await Assert
                .That(async () =>
                {
                    await node.ConnectAsync(777, 556).ConfigureAwait(false);
                })
                .Throws<InvalidOperationException>();

            var second = await node.ConnectAsync(777, 555).ConfigureAwait(false);
            await Assert.That(second).IsNotNull();
            await Assert.That(second!.GuildId).IsEqualTo(777);
            await Assert.That(ReferenceEquals(first, second)).IsTrue();

            var live = new TaskCompletionSource<bool>();
            first.PlayerUpdated += (_, _) =>
            {
                live.TrySetResult(true);
                return Task.CompletedTask;
            };
            await server
                .PushAsync(
                    """{"op":"playerUpdate","guildId":"777","state":{"time":1,"position":2,"connected":true,"ping":3}}"""
                )
                .ConfigureAwait(false);
            await Assert
                .That(await live.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsTrue();

            bridge.FireVoiceState(777, null, "vsess");
            await Task.Delay(200).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task DropAndResume()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (extension, node, server) = (stack.Extension, stack.Node, stack.Server);

            var dropped = new TaskCompletionSource<bool>();
            extension.NodeDisconnected += (_, args) =>
            {
                dropped.TrySetResult(args.Final);
                return Task.CompletedTask;
            };
            await server.DropConnectionAsync(1000, "server restart").ConfigureAwait(false);
            await Assert
                .That(await dropped.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                .IsFalse();
            await Assert.That(node.IsConnected).IsFalse();

            await Assert
                .That(() => node.IsConnected && node.IsReady)
                .WaitsFor(assert => assert.IsTrue(), TimeSpan.FromSeconds(10));
            await Assert.That(server.LastResumeFlag).IsTrue();
        }
    }

    [Test]
    public async Task ErrorPaths()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;
            var server = stack.Server;

            server.On("GET", "/v4/stats", _ => new FakeRestResponse(500, "{\"message\":\"boom\"}"));
            var rest = await Assert
                .That(async () => await node.GetStatsAsync().ConfigureAwait(false))
                .Throws<RestException>()
                .WithMessageContaining("boom");
            await Assert.That(rest!.StatusCode).IsEqualTo(500);

            var wrong = new Node(
                new Endpoint("127.0.0.1", server.Port, "wrong-password"),
                new NodeOptions { Name = "wrong", ReadyTimeout = TimeSpan.FromSeconds(5) }
            );
            var unauthorized = await Assert
                .That(async () =>
                    await wrong
                        .EnsureServerInfoAsync(CancellationToken.None, forceProbe: true)
                        .ConfigureAwait(false)
                )
                .Throws<RestException>();
            await Assert.That(unauthorized!.StatusCode).IsEqualTo(401);

            var incompatibleServer = new FakeNodeServer { InfoStatus = 404 };
            try
            {
                var incompatible = new Node(
                    new Endpoint("127.0.0.1", incompatibleServer.Port, "youshallnotpass"),
                    new NodeOptions { Name = "old", ReadyTimeout = TimeSpan.FromSeconds(5) }
                );
                await Assert
                    .That(async () =>
                    {
                        await incompatible
                            .EnsureServerInfoAsync(CancellationToken.None, forceProbe: true)
                            .ConfigureAwait(false);
                    })
                    .Throws<IncompatibleServerException>();
            }
            finally
            {
                incompatibleServer.Dispose();
            }

            var silent = new FakeNodeServer { InfoStatus = 200, InfoBody = "{}" };
            try
            {
                var versionless = new Node(
                    new Endpoint("127.0.0.1", silent.Port, "youshallnotpass"),
                    new NodeOptions { Name = "silent", ReadyTimeout = TimeSpan.FromSeconds(5) }
                );
                await Assert
                    .That(async () =>
                    {
                        await versionless
                            .EnsureServerInfoAsync(CancellationToken.None, forceProbe: true)
                            .ConfigureAwait(false);
                    })
                    .Throws<IncompatibleServerException>();
            }
            finally
            {
                silent.Dispose();
            }
        }
    }
}
