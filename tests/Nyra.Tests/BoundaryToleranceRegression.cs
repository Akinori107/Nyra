using System.Text.Json;
using Nyra.Entities;
using Nyra.Exceptions;

namespace Nyra.Tests;

public sealed class BoundaryToleranceRegression
{
    [Test]
    public async Task RoundedInt64ExactBoundary()
    {
        var options = NyraJson.SerializerOptions;
        var chapter = JsonSerializer.Deserialize<Chapter>(
            """{"startTime":0,"duration":9007199254740993}""",
            options
        );
        await Assert.That(chapter?.Duration).IsEqualTo(9_007_199_254_740_993L);
        var fractional = JsonSerializer.Deserialize<Chapter>(
            """{"startTime":0,"duration":1500.6}""",
            options
        );
        await Assert.That(fractional?.Duration).IsEqualTo(1501);
    }

    [Test]
    public async Task TrackStreamWhitespaceUrlRejects()
    {
        var server = new FakeNodeServer();
        server.On("GET", "/v4/trackstream", 200, """{"protocol":"http","url":"   "}""");
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "stream-blank",
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
                ReadyTimeout = TimeSpan.FromSeconds(5),
            }
        );
        extension.AddNode(node);
        await extension.ConnectAllNodesAsync().ConfigureAwait(false);

        var track = JsonSerializer.Deserialize<Track>(
            """{"encoded":"AAA","title":"T"}""",
            NyraJson.SerializerOptions
        )!;
        await Assert
            .That(async () =>
            {
                await node.Tracks.GetStreamUrlAsync(track, itag: 140).ConfigureAwait(false);
            })
            .Throws<RestException>();

        await extension.DisposeAsync().ConfigureAwait(false);
        server.Dispose();
    }
}
