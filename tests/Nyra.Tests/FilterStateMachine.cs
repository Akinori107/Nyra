using System.Text.Json;
using Nyra.Entities;
using Nyra.Entities.Filters;

namespace Nyra.Tests;

public sealed class FilterStateMachine
{
    private const string PlayerPath = "/v4/sessions/fake-session/players/777";

    private static string PlayerBody(string? filtersJson) =>
        $$"""
            {"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}{{(
                filtersJson is null ? string.Empty : $",\"filters\":{filtersJson}"
            )}}}
            """;

    private static string DesiredJson => """{"tremolo":{"frequency":8,"depth":0.5}}""";

    private static FilterChain Desired() =>
        new()
        {
            Tremolo = new TremoloFilter { Frequency = 8, Depth = 0.5f },
        };

    private static string DivergentJson => """{"tremolo":{"frequency":5,"depth":0.5}}""";

    private static async Task<Stack> StartAsync(FakeNodeServer? server = null)
    {
        server ??= new FakeNodeServer();
        server.On("PATCH", "/v4/sessions/fake-session", 200, """{"resuming":true,"timeout":60}""");
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "fsm",
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

    private static async Task<GuildConnection> ConnectGuildAsync(
        FakeBridge bridge,
        Node node,
        FakeNodeServer server,
        string? echoJson
    )
    {
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(200, PlayerBody(echoJson)));
        var connect = node.ConnectAsync(777, 555);
        await Task.Delay(100).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        return await connect.ConfigureAwait(false);
    }

    private static void Echo(FakeNodeServer server, string? echoJson) =>
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(200, PlayerBody(echoJson)));

    [Test]
    public async Task AppliedFollowsTheEcho()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server, DivergentJson)
                .ConfigureAwait(false);

            await conn.Filters.ApplyAsync(Desired()).ConfigureAwait(false);
            await Assert.That(conn.Filters.Applied.Tremolo?.Frequency).IsEqualTo(5);

            Echo(server, DesiredJson);
            await conn.Filters.ApplyAsync(Desired()).ConfigureAwait(false);
            await Assert.That(conn.Filters.Applied.Tremolo?.Frequency).IsEqualTo(8);
        }
    }

    [Test]
    public async Task CaptureFencing()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server, DivergentJson)
                .ConfigureAwait(false);
            var filters = conn.Filters;

            await conn.Filters.ApplyAsync(Desired()).ConfigureAwait(false);
            await Assert.That(conn.Filters.Applied.Tremolo?.Frequency).IsEqualTo(5);

            static Player CapturePlayer(string filtersJson) =>
                JsonSerializer.Deserialize<Player>(
                    "{\"guildId\":\"777\",\"filters\":" + filtersJson + "}",
                    NyraJson.SerializerOptions
                )!;

            var staleTicket = GuildFilters.NextTicket() - 10;
            await filters
                .CaptureServerFiltersAsync(
                    CapturePlayer("""{"tremolo":{"frequency":2,"depth":0.5}}"""),
                    staleTicket,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            await Assert.That(conn.Filters.Applied.Tremolo?.Frequency).IsEqualTo(5);

            var freshTicket = GuildFilters.NextTicket();
            await filters
                .CaptureServerFiltersAsync(
                    CapturePlayer("""{"tremolo":{"frequency":3,"depth":0.5}}"""),
                    freshTicket,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            await Assert.That(conn.Filters.Applied.Tremolo?.Frequency).IsEqualTo(3);

            await filters
                .CaptureServerFiltersAsync(
                    CapturePlayer("""{"tremolo":{"frequency":2,"depth":0.5}}"""),
                    staleTicket,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            await Assert.That(conn.Filters.Applied.Tremolo?.Frequency).IsEqualTo(3);

            var emptyTicket = GuildFilters.NextTicket();
            await filters
                .CaptureServerFiltersAsync(
                    JsonSerializer.Deserialize<Player>(
                        """{"guildId":"777"}""",
                        NyraJson.SerializerOptions
                    )!,
                    emptyTicket,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            await filters
                .CaptureServerFiltersAsync(
                    CapturePlayer("""{"tremolo":{"frequency":9,"depth":0.5}}"""),
                    emptyTicket - 1,
                    CancellationToken.None
                )
                .ConfigureAwait(false);
            await Assert.That(conn.Filters.Applied.Tremolo?.Frequency).IsEqualTo(3);
        }
    }

    [Test]
    public async Task ClearResetsState()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server, DivergentJson)
                .ConfigureAwait(false);
            var filters = conn.Filters;

            await conn.Filters.ApplyAsync(Desired()).ConfigureAwait(false);

            await filters.ResetAsync().ConfigureAwait(false);
            await Assert.That(conn.Filters.Applied.Tremolo).IsNull();
        }
    }

    [Test]
    public async Task ConfigDisabledGuard()
    {
        var stack = await StartAsync(
                new FakeNodeServer
                {
                    IsNodeLink = false,
                    Semver = "4.2.0",
                    InfoBody =
                        """{"version":{"semver":"4.2.0"},"plugins":[],"filters":["volume","karaoke"]}""",
                }
            )
            .ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server, echoJson: null)
                .ConfigureAwait(false);
            await Assert.That(node.IsNodeLink).IsFalse();

            await conn.Filters.ApplyAsync(new FilterChain { Volume = 2 }).ConfigureAwait(false);
            await Assert.That(conn.Filters.Applied.Volume).IsEqualTo(2);
            await conn
                .Filters.ApplyAsync(
                    new FilterChain { Karaoke = new KaraokeFilter { Level = 0.8f } }
                )
                .ConfigureAwait(false);
            await Assert.That(conn.Filters.Applied.Karaoke).IsNotNull();
            await Assert.That(conn.Filters.Applied.Karaoke!.Level).IsEqualTo(0.8f);

            await Assert
                .That(async () =>
                {
                    await conn
                        .Filters.ApplyAsync(
                            new FilterChain
                            {
                                Karaoke = new KaraokeFilter { Level = 0.8f },
                                Equalizer = [new EqualizerBand(0, 0.5f)],
                            }
                        )
                        .ConfigureAwait(false);
                })
                .Throws<NotSupportedException>();

            var cases = new FilterChain[]
            {
                new() { Equalizer = [new EqualizerBand(1, 0.25f)] },
                new() { Timescale = new TimescaleFilter { Speed = 1.5 } },
                new()
                {
                    Tremolo = new TremoloFilter { Frequency = 8, Depth = 0.5f },
                },
                new()
                {
                    Vibrato = new VibratoFilter { Frequency = 8, Depth = 0.5f },
                },
                new() { Distortion = new DistortionFilter { SinOffset = 1 } },
                new() { Rotation = new RotationFilter { RotationHz = 1.5 } },
                new() { ChannelMix = new ChannelMixFilter { LeftToLeft = 1 } },
                new() { LowPass = new LowPassFilter { Smoothing = 1 } },
            };
            foreach (var chain in cases)
                await Assert
                    .That(async () =>
                    {
                        await conn.Filters.ApplyAsync(chain).ConfigureAwait(false);
                    })
                    .Throws<NotSupportedException>();

            using (Assert.Multiple())
            {
                await Assert.That(conn.Filters.Applied.Karaoke).IsNotNull();
                await Assert.That(conn.Filters.Applied.Karaoke!.Level).IsEqualTo(0.8f);
                await Assert.That(conn.Filters.Applied.Equalizer).IsNull();
                await Assert.That(conn.Filters.Applied.Tremolo).IsNull();
            }

            await Assert
                .That(async () =>
                {
                    await conn
                        .Filters.ApplyAsync(
                            new FilterChain { Tesseract = new TesseractFilter { RotationHz = 1 } }
                        )
                        .ConfigureAwait(false);
                })
                .Throws<NotSupportedException>();
        }
    }

    [Test]
    public async Task ConfigDisabledValueMembers()
    {
        var stack = await StartAsync(
                new FakeNodeServer
                {
                    IsNodeLink = false,
                    Semver = "4.2.0",
                    InfoBody = """{"version":{"semver":"4.2.0"},"plugins":[],"filters":[]}""",
                }
            )
            .ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server, echoJson: null)
                .ConfigureAwait(false);

            await Assert
                .That(async () =>
                {
                    await conn
                        .Filters.ApplyAsync(new FilterChain { Volume = 2 })
                        .ConfigureAwait(false);
                })
                .Throws<NotSupportedException>();

            await Assert
                .That(async () =>
                {
                    await conn
                        .Filters.ApplyAsync(
                            new FilterChain { Karaoke = new KaraokeFilter { Level = 0.9f } }
                        )
                        .ConfigureAwait(false);
                })
                .Throws<NotSupportedException>();
            await Assert.That(conn.Filters.Applied.Volume).IsNull();
            await Assert.That(conn.Filters.Applied.Karaoke).IsNull();
        }
    }

    [Test]
    public async Task ValidationFiniteEdges()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server, echoJson: null)
                .ConfigureAwait(false);

            var cases = new FilterChain[]
            {
                new()
                {
                    Chorus = new ChorusFilter
                    {
                        Rate = 2,
                        Depth = 0.5f,
                        Mix = 0.5f,
                        Feedback = float.NaN,
                    },
                },
                new()
                {
                    Chorus = new ChorusFilter
                    {
                        Rate = 2,
                        Depth = 0.5f,
                        Mix = 0.5f,
                        DelayMilliseconds = float.NaN,
                    },
                },
                new() { Timescale = new TimescaleFilter { Speed = 0 } },
                new()
                {
                    Phaser = new PhaserFilter
                    {
                        Rate = 1,
                        Depth = 0.5f,
                        Mix = 0.5f,
                        Feedback = float.NaN,
                    },
                },
                new() { Rotation = new RotationFilter { RotationHz = double.NaN } },
            };
            foreach (var chain in cases)
                await Assert
                    .That(async () =>
                    {
                        await conn.Filters.ApplyAsync(chain).ConfigureAwait(false);
                    })
                    .Throws<ArgumentOutOfRangeException>();

            await Assert
                .That(async () =>
                {
                    await conn
                        .Filters.AddAsync(new TesseractFilter { RotationHz = double.NaN })
                        .ConfigureAwait(false);
                })
                .Throws<ArgumentOutOfRangeException>();
        }
    }
}
