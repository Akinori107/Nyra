using System.Net.Http.Headers;
using static Nyra.Tests.NodeHarness;

namespace Nyra.Tests;

public sealed class AdapterHappyPathAndTeardown
{
    private const string PlayerJson =
        """{"guildId":"777","track":null,"volume":100,"paused":false,"state":{"time":0,"position":0,"connected":true,"ping":42},"voice":{"token":"tok","endpoint":"127.0.0.1","sessionId":"vsess"}}""";

    private sealed class Stack : IAsyncDisposable
    {
        public required FakeBridge Bridge { get; init; }

        public required NyraExtension Extension { get; init; }

        public required Node Node { get; init; }

        public required FakeNodeServer Server { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Extension.DisposeAsync().ConfigureAwait(false);
            Server.Dispose();
        }
    }

    private static async Task<Stack> StartAsync()
    {
        var server = new FakeNodeServer();
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "adapter-teardown",
                Resuming = true,
                ReconnectDelay = TimeSpan.FromMilliseconds(50),
                MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
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
    public async Task RetryAfterFallThrough()
    {
        using var response = new HttpResponseMessage();
        response.Headers.RetryAfter = new RetryConditionHeaderValue(
            DateTimeOffset.UtcNow - TimeSpan.FromHours(1)
        );
        var read = Internal.RestClient.ReadRetryAfter(response, TimeProvider.System);
        await Assert.That(read).IsNull();
    }

    [Test]
    public async Task InfoSecondDocument()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            stack.Server.On("GET", "/v4/info", 200, "{} {}");
            var rejected = await Assert
                .That(async () =>
                    await stack
                        .Node.Rest.Info.GetServerInfoAsync(CancellationToken.None)
                        .ConfigureAwait(false)
                )
                .Throws<Exceptions.IncompatibleServerException>();
            await Assert.That(rejected!.InnerException).IsTypeOf<System.Text.Json.JsonException>();
        }
    }

    [Test]
    public async Task ReconcilePausePoll()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (bridge, node, server) = (stack.Bridge, stack.Node, stack.Server);
            var conn = await ConnectGuildAsync(bridge, node, server).ConfigureAwait(false);

            server.On("GET", PlayerPath, _ => new FakeRestResponse(200, PlayerJson));
            server.OnRaw("PATCH", PlayerPath, async (_, raw) =>
            {
                await raw.DisposeAsync().ConfigureAwait(false);
            });

            await Assert
                .That(async () => await conn.PauseAsync().ConfigureAwait(false))
                .Throws<Exceptions.NodeUnreachableException>();
            await Assert
                .That(server.RestCalls.Any(c => c is { Method: "GET", Path: PlayerPath }))
                .IsTrue();
        }
    }
}
