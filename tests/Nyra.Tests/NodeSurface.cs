namespace Nyra.Tests;

public sealed class NodeSurface
{
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

    private static NodeOptions BaseOptions() =>
        new()
        {
            Name = "surface",
            ReconnectDelay = TimeSpan.FromMilliseconds(50),
            MaxReconnectDelay = TimeSpan.FromMilliseconds(200),
            VoiceHandshakeTimeout = TimeSpan.FromSeconds(5),
            ReadyTimeout = TimeSpan.FromSeconds(5),
        };

    private static TestDataRow<NodeOptions> Case(NodeOptions options, string name) =>
        new(options, DisplayName: name);

    public static IEnumerable<TestDataRow<NodeOptions>> OptionGuards()
    {
        yield return Case(new NodeOptions { Name = string.Empty }, "a blank name");
        yield return Case(
            new NodeOptions
            {
                Name = "x",
                ReconnectDelay = TimeSpan.Zero,
                MaxReconnectDelay = TimeSpan.Zero,
            },
            "a zero reconnect delay"
        );
        yield return Case(
            new NodeOptions
            {
                Name = "x",
                ReconnectDelay = TimeSpan.FromSeconds(2),
                MaxReconnectDelay = TimeSpan.FromSeconds(1),
            },
            "a max below the reconnect delay"
        );
        yield return Case(
            new NodeOptions { Name = "x", MaxReconnectDelay = TimeSpan.FromDays(41) },
            "a max reconnect delay past the ceiling"
        );
        yield return Case(
            new NodeOptions { Name = "x", ResumeTimeout = TimeSpan.FromSeconds(-1) },
            "a negative resume timeout"
        );
        yield return Case(
            new NodeOptions
            {
                Name = "x",
                ResumeTimeout = TimeSpan.FromSeconds((double)int.MaxValue + 1),
            },
            "a resume timeout past the wire ceiling"
        );
        yield return Case(
            new NodeOptions { Name = "x", VoiceHandshakeTimeout = TimeSpan.Zero },
            "a zero voice handshake timeout"
        );
        yield return Case(
            new NodeOptions { Name = "x", VoiceHandshakeTimeout = TimeSpan.FromDays(41) },
            "a voice handshake timeout past the ceiling"
        );
        yield return Case(
            new NodeOptions { Name = "x", ReadyTimeout = TimeSpan.Zero },
            "a zero ready timeout"
        );
        yield return Case(
            new NodeOptions { Name = "x", RescueTimeout = TimeSpan.Zero },
            "a zero rescue timeout"
        );
        yield return Case(
            new NodeOptions { Name = "x", RescueTimeout = TimeSpan.FromSeconds(-1) },
            "a negative rescue timeout"
        );
        yield return Case(
            new NodeOptions { Name = "x", ReadyTimeout = TimeSpan.FromDays(41) },
            "a ready timeout past the ceiling"
        );
        yield return Case(
            new NodeOptions { Name = "x", FailoverAfter = TimeSpan.FromSeconds(-1) },
            "a negative failover delay"
        );
        yield return Case(
            new NodeOptions { Name = "x", FailoverAfter = TimeSpan.FromDays(41) },
            "a failover delay past the ceiling"
        );
    }

    [Test]
    [MethodDataSource(nameof(OptionGuards))]
    public async Task CtorOptionGuards(NodeOptions options)
    {
        await Assert
            .That(() =>
            {
                _ = new Node(new Endpoint("127.0.0.1", 1, "pass"), options);
            })
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task PluginProbe()
    {
        var bare = new Node(new Endpoint("127.0.0.1", 1, "pass"), BaseOptions());
        await Assert.That(bare.HasPlugin("anything")).IsFalse();
        await Assert
            .That(() =>
            {
                bare.HasPlugin(null!);
            })
            .Throws<ArgumentNullException>();

        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            await Assert.That(stack.Node.HasPlugin("lavasrc-plugin")).IsTrue();
            await Assert.That(stack.Node.HasPlugin("other-plugin")).IsFalse();
        }
    }

    [Test]
    public async Task DisposeTails()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var node = stack.Node;
            await node.DisposeAsync().ConfigureAwait(false);

            await Assert
                .That(async () => await node.DisposeAsync().ConfigureAwait(false))
                .ThrowsNothing();

            await Assert
                .That(async () =>
                {
                    await node.LoadTracksAsync("atwa").ConfigureAwait(false);
                })
                .Throws<ObjectDisposedException>();
        }
    }

    private static async Task<Stack> StartAsync(FakeNodeServer? server = null)
    {
        server ??= new FakeNodeServer
        {
            InfoBody =
                """{"version":{"semver":"3.10.0"},"isNodelink":true,"plugins":[{"name":"lavasrc-plugin"}],"filters":[]}""",
        };
        var bridge = new FakeBridge();
        bridge.GoReady();
        var extension = new NyraExtension(bridge);
        var node = new Node(
            new Endpoint("127.0.0.1", server.Port, server.Password),
            new NodeOptions
            {
                Name = "surface",
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
            Extension = extension,
            Node = node,
            Server = server,
        };
    }
}
