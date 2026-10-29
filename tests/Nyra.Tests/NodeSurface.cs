using System.Text.Json;
using Nyra.Entities;

namespace Nyra.Tests;

public sealed class NodeSurface
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
            new NodeOptions { Name = "x", ReadyTimeout = TimeSpan.FromDays(41) },
            "a ready timeout past the ceiling"
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
    public async Task GroupSurface()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var (node, server) = (stack.Node, stack.Server);
            var track = JsonSerializer.Deserialize<Track>(TrackJson, NyraJson.SerializerOptions)!;
            const string groupPath = "/v4/sessions/fake-session/groups/g1";

            string PatchBody(string body) =>
                server.RestCalls.Last(c => c is { Method: "PATCH", Path: groupPath }).Body
                ?? string.Empty;

            server.On(
                "GET",
                "/v4/sessions/fake-session/groups",
                200,
                """[{"id":"g1","createdAt":123}]"""
            );
            var groups = await node.Groups.ListAsync().ConfigureAwait(false);
            await Assert.That(groups.Count).IsEqualTo(1);
            await Assert.That(groups[0].Id).IsEqualTo("g1");

            var created = new TaskCompletionSource<string?>();
            server.On(
                "POST",
                "/v4/sessions/fake-session/groups",
                call =>
                {
                    created.TrySetResult(call.Body);
                    return new FakeRestResponse(
                        200,
                        """{"id":"g2","guildIds":[777],"createdAt":1}"""
                    );
                }
            );
            var made = await node
                .Groups.CreateAsync("g2", new List<ulong> { 777 }.Where(_ => true))
                .ConfigureAwait(false);
            await Assert.That(made.Id).IsEqualTo("g2");
            await Assert
                .That(
                    (
                        await created.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false)
                    )?.Contains("777")
                )
                .IsTrue();

            server.On("GET", groupPath, 200, """{"id":"g1","createdAt":5}""");
            await Assert
                .That((await node.Groups.GetAsync("g1").ConfigureAwait(false))?.Id)
                .IsEqualTo("g1");
            server.On("GET", groupPath, 404, """{"message":"not found"}""");
            await Assert.That(await node.Groups.GetAsync("g1").ConfigureAwait(false)).IsNull();

            server.On("DELETE", groupPath, 204, string.Empty);
            await node.Groups.DeleteAsync("g1").ConfigureAwait(false);
            await Assert
                .That(server.RestCalls)
                .Contains(c => c is { Method: "DELETE", Path: groupPath });

            server.On(
                "PATCH",
                groupPath,
                _ => new FakeRestResponse(200, """{"id":"g1","createdAt":1}""")
            );
            await node.Groups.StopAsync("g1").ConfigureAwait(false);
            await Assert.That(PatchBody(string.Empty)).Contains("\"encoded\":null");
            await node.Groups.PauseAsync("g1").ConfigureAwait(false);
            await Assert.That(PatchBody(string.Empty)).Contains("\"paused\":true");
            await node.Groups.ResumeAsync("g1").ConfigureAwait(false);
            await Assert.That(PatchBody(string.Empty)).Contains("\"paused\":false");
            await node.Groups.SetVolumeAsync("g1", 250).ConfigureAwait(false);
            await Assert.That(PatchBody(string.Empty)).Contains("\"volume\":250");

            await Assert
                .That(async () =>
                {
                    await node.Groups.SetVolumeAsync("g1", -5).ConfigureAwait(false);
                })
                .Throws<ArgumentOutOfRangeException>();

            await Assert
                .That(async () =>
                {
                    await node.Groups.PlayTrackAsync("g1", null!).ConfigureAwait(false);
                })
                .Throws<ArgumentNullException>();
            await Assert
                .That(async () =>
                {
                    await node.Groups.PlayTrackAsync("g1", new Track()).ConfigureAwait(false);
                })
                .Throws<ArgumentException>();
            await Assert
                .That(async () =>
                {
                    await node
                        .Groups.PlayTrackAsync("g1", track, position: TimeSpan.FromMilliseconds(-1))
                        .ConfigureAwait(false);
                })
                .Throws<ArgumentOutOfRangeException>();
            await Assert
                .That(async () =>
                {
                    await node
                        .Groups.PlayTrackAsync("g1", track, endTime: TimeSpan.FromMilliseconds(-1))
                        .ConfigureAwait(false);
                })
                .Throws<ArgumentOutOfRangeException>();

            await node
                .Groups.PlayTrackAsync("g1", track, position: TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
            var playBody = PatchBody(string.Empty);
            await Assert.That(playBody).Contains("QXRsZWFzdA==");
            await Assert.That(playBody).Contains("\"position\":2000");

            await Assert
                .That(async () =>
                {
                    await node.Groups.AddPlayersAsync("g1", null!).ConfigureAwait(false);
                })
                .Throws<ArgumentNullException>();
            await Assert
                .That(async () =>
                {
                    await node.Groups.RemovePlayersAsync("g1", null!).ConfigureAwait(false);
                })
                .Throws<ArgumentNullException>();

            await node
                .Groups.AddPlayersAsync("g1", new List<ulong> { 111 }.Where(_ => true))
                .ConfigureAwait(false);
            await Assert.That(PatchBody(string.Empty)).Contains("\"add\":[\"111\"]");
            await node.Groups.RemovePlayersAsync("g1", [222]).ConfigureAwait(false);
            await Assert.That(PatchBody(string.Empty)).Contains("\"remove\":[\"222\"]");

            string? lastSessionBody = null;
            server.On(
                "PATCH",
                "/v4/sessions/fake-session",
                call =>
                {
                    lastSessionBody = call.Body;
                    return new FakeRestResponse(200, """{"resuming":true,"timeout":90}""");
                }
            );
            var fractional = await node.UpdateSessionAsync(true, TimeSpan.FromMilliseconds(1500))
                .ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(fractional).IsNotNull();
                await Assert.That(fractional!.Resuming).IsTrue();
                await Assert.That(fractional!.Timeout).IsEqualTo(90);
            }
            await Assert.That(lastSessionBody?.Contains("\"timeout\":1")).IsTrue();
            await Assert
                .That(async () =>
                {
                    await node.UpdateSessionAsync(true, TimeSpan.FromSeconds(-1))
                        .ConfigureAwait(false);
                })
                .Throws<ArgumentOutOfRangeException>();
            var session = await node.UpdateSessionAsync(true, TimeSpan.FromSeconds(90))
                .ConfigureAwait(false);
            using (Assert.Multiple())
            {
                await Assert.That(session).IsNotNull();
                await Assert.That(session!.Resuming).IsTrue();
                await Assert.That(session!.Timeout).IsEqualTo(90);
            }
            await Assert.That(lastSessionBody?.Contains("\"timeout\":90")).IsTrue();
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
