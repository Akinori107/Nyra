using Nyra.Entities;
using Nyra.Entities.Filters;
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
        await WaitForJoinAsync(bridge).ConfigureAwait(false);
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
                await Assert
                    .That(async () =>
                    {
                        await conn.Filters.AddAsync(filter).ConfigureAwait(false);
                    })
                    .Throws<ArgumentOutOfRangeException>();
        }
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
            var body = lavaStack
                .Server.RestCalls.Last(c => c is { Method: "PATCH", Path: PlayerPath })
                .Body
                ?? string.Empty;
            await Assert.That(body).Contains("\"volume\":2.5");

            foreach (var bad in new[] { -1f, 6f, float.NaN })
                await Assert
                    .That(async () =>
                    {
                        await lavaConn.Filters.SetVolumeFilterAsync(bad).ConfigureAwait(false);
                    })
                    .Throws<ArgumentOutOfRangeException>();
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
