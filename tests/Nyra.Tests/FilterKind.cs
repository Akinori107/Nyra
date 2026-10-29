using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.Internal;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class FilterKind
{
    private const string PlayerBody =
        """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}""";

    private sealed record FakeFilter : IFilter;

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
                Name = "kinds",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);
        server.On("PATCH", PlayerPath, _ => new FakeRestResponse(200, PlayerBody));
        var connect = node.ConnectAsync(777, 555);
        await Task.Delay(100).ConfigureAwait(false);
        bridge.FireVoiceState(777, 555, "vsess");
        bridge.FireVoiceServer(777, "tok", "127.0.0.1");
        await connect.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        return new Stack
        {
            Bridge = bridge,
            Extension = extension,
            Node = node,
            Server = server,
        };
    }

    [Test]
    public async Task AddRemove()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;
            var server = stack.Server;

            var kinds = new (IFilter Filter, string Wire)[]
            {
                (new KaraokeFilter { Level = 0.8f }, "karaoke"),
                (new TimescaleFilter { Speed = 1.5 }, "timescale"),
                (new TremoloFilter { Frequency = 8, Depth = 0.5f }, "tremolo"),
                (new VibratoFilter { Frequency = 8, Depth = 0.5f }, "vibrato"),
                (new DistortionFilter { SinOffset = 1 }, "distortion"),
                (new RotationFilter { RotationHz = 1.5 }, "rotation"),
                (new ChannelMixFilter { LeftToLeft = 1 }, "channelMix"),
                (new LowPassFilter { Smoothing = 1 }, "lowPass"),
                (new EchoFilter { DelayMilliseconds = 200, Mix = 0.5f }, "echo"),
                (new ReverbFilter { RoomSize = 0.5f }, "reverb"),
                (new HighPassFilter { Smoothing = 1 }, "highpass"),
                (
                    new ChorusFilter
                    {
                        Rate = 2,
                        Depth = 0.5f,
                        Feedback = 0.5f,
                        DelayMilliseconds = 20,
                    },
                    "chorus"
                ),
                (new PhaserFilter { Rate = 2, Feedback = 0.5f }, "phaser"),
                (new FlangerFilter { Rate = 2, Depth = 0.5f }, "flanger"),
                (new SpatialFilter { X = 0.5f }, "spatial"),
                (new CompressorFilter { Threshold = -20f, Ratio = 4f }, "compressor"),
                (new PhonographFilter { Frequency = 1000 }, "phonograph"),
                (new TesseractFilter { RotationHz = 1 }, "tesseract"),
            };
            foreach (var (filter, wire) in kinds)
            {
                await conn.Filters.AddAsync(filter).ConfigureAwait(false);
                var addBody =
                    server.RestCalls.Last(c => c is { Method: "PATCH", Path: PlayerPath }).Body
                    ?? string.Empty;
                await Assert.That(addBody).Contains($"\"{wire}\":{{");

                await conn.Filters.RemoveAsync(filter).ConfigureAwait(false);
                var removeBody =
                    server.RestCalls.Last(c => c is { Method: "PATCH", Path: PlayerPath }).Body
                    ?? string.Empty;
                await Assert.That(removeBody).Contains($"\"{wire}\":null");

                await conn.Filters.AddAsync(filter).ConfigureAwait(false);
                await RemoveByTypeAsync(conn, filter.GetType()).ConfigureAwait(false);
                var genericBody =
                    server.RestCalls.Last(c => c is { Method: "PATCH", Path: PlayerPath }).Body
                    ?? string.Empty;
                await Assert.That(genericBody).Contains($"\"{wire}\":null");
            }
        }
    }

    private static async Task RemoveByTypeAsync(GuildConnection conn, Type type)
    {
        var method = typeof(GuildFilters)
            .GetMethods()
            .First(m => m.Name == "RemoveAsync" && m.IsGenericMethod);
        var closed = method.MakeGenericMethod(type);
        var task = (Task)closed.Invoke(conn.Filters, [default(CancellationToken)])!;
        await task.ConfigureAwait(false);
    }

    [Test]
    public async Task UnsupportedTypeGuards()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;

            await Assert
                .That(async () =>
                {
                    await conn.Filters.AddAsync(new FakeFilter()).ConfigureAwait(false);
                })
                .Throws<ArgumentException>();

            var genericRejected = await Assert
                .That(async () => await RemoveFakeAsync(conn).ConfigureAwait(false))
                .Throws<ArgumentException>();
            await Assert.That(genericRejected!.ParamName).IsNull();

            var instanceRejected = await Assert
                .That(async () =>
                    await conn.Filters.RemoveAsync(new FakeFilter()).ConfigureAwait(false)
                )
                .Throws<ArgumentException>();
            await Assert.That(instanceRejected!.ParamName).IsEqualTo("filter");

            await Assert
                .That(async () =>
                {
                    await conn.Filters.RemoveAsync(null!).ConfigureAwait(false);
                })
                .Throws<ArgumentNullException>();
        }
    }

    private static async Task RemoveFakeAsync(GuildConnection conn)
    {
        var method = typeof(GuildFilters)
            .GetMethods()
            .First(m => m.Name == "RemoveAsync" && m.IsGenericMethod);
        var closed = method.MakeGenericMethod(typeof(FakeFilter));
        var task = (Task)closed.Invoke(conn.Filters, [default(CancellationToken)])!;
        await task.ConfigureAwait(false);
    }

    [Test]
    public async Task NonFinite()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;

            var cases = new IFilter[]
            {
                new KaraokeFilter { Level = float.NaN },
                new TimescaleFilter { Speed = double.NaN },
                new TremoloFilter { Frequency = float.NaN },
                new VibratoFilter { Frequency = float.NaN },
                new DistortionFilter { SinOffset = float.NaN },
                new RotationFilter { RotationHz = double.NaN },
                new ChannelMixFilter { LeftToLeft = float.NaN },
                new LowPassFilter { Smoothing = float.NaN },
                new EchoFilter { DelayMilliseconds = float.NaN },
                new ReverbFilter { RoomSize = float.NaN },
                new HighPassFilter { Smoothing = float.NaN },
                new ChorusFilter { Rate = float.NaN },
                new PhaserFilter { Rate = float.NaN },
                new FlangerFilter { Rate = float.NaN },
                new SpatialFilter { X = float.NaN },
                new CompressorFilter { Threshold = float.NaN },
                new PhonographFilter { Frequency = float.NaN },
                new TesseractFilter { RotationHz = double.NaN },
            };
            foreach (var filter in cases)
            {
                await Assert
                    .That(async () =>
                    {
                        await conn.Filters.AddAsync(filter).ConfigureAwait(false);
                    })
                    .Throws<ArgumentOutOfRangeException>();
            }
        }
    }

    [Test]
    public async Task HighPassChainBounds()
    {
        var touched = new FilterChain { HighPass = new HighPassFilter(Smoothing: -1f) };
        await Assert
            .That(() => FilterValidation.ValidateFilterBounds(touched, nodeLink: true))
            .Throws<ArgumentOutOfRangeException>();
        await Assert
            .That(() => FilterValidation.ValidateFilterBounds(touched, touched, nodeLink: true))
            .ThrowsNothing();
    }

    [Test]
    public async Task VolumeFilterGate()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;
            await Assert
                .That(async () =>
                {
                    await conn.Filters.SetVolumeFilterAsync(2.5f).ConfigureAwait(false);
                })
                .Throws<NotSupportedException>();
        }

        var lavaStack = await StartAsync(
                new FakeNodeServer { IsNodeLink = false, Semver = "4.2.0" }
            )
            .ConfigureAwait(false);
        await using (lavaStack.ConfigureAwait(false))
        {
            var lavaConn = lavaStack.Node.GetConnection(777)!;
            await lavaConn.Filters.SetVolumeFilterAsync(2.5f).ConfigureAwait(false);
            var body =
                lavaStack
                    .Server.RestCalls.Last(c => c is { Method: "PATCH", Path: PlayerPath })
                    .Body
                ?? string.Empty;
            await Assert.That(body).Contains("\"volume\":2.5");

            foreach (var bad in new[] { -1f, 6f, float.NaN })
            {
                await Assert
                    .That(async () =>
                    {
                        await lavaConn.Filters.SetVolumeFilterAsync(bad).ConfigureAwait(false);
                    })
                    .Throws<ArgumentOutOfRangeException>();
            }
        }
    }

    [Test]
    public async Task ConfigGateRemainder()
    {
        var stack = await StartAsync(
                new FakeNodeServer
                {
                    IsNodeLink = false,
                    Semver = "4.2.0",
                    InfoBody =
                        """{"version":{"semver":"4.2.0"},"plugins":[],"filters":["karaoke"]}""",
                }
            )
            .ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var conn = stack.Node.GetConnection(777)!;

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
                            new FilterChain
                            {
                                Echo = new EchoFilter { DelayMilliseconds = 200, Mix = 0.5f },
                            }
                        )
                        .ConfigureAwait(false);
                })
                .Throws<NotSupportedException>();
        }
    }
}
