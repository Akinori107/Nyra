using DSharpPlus;
using Nyra.DSharpPlus;
using Nyra.Entities;

namespace Nyra.Tests;

public sealed class RestAndAdapterRegression
{
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
                Name = "hunt-v89",
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

    [Test]
    public async Task LoadUnknownSearchType()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            var unknown = await Assert
                .That(async () =>
                    await stack
                        .Node.LoadTracksAsync("anything", (SearchType)999)
                        .ConfigureAwait(false)
                )
                .Throws<ArgumentOutOfRangeException>();
            await Assert.That(unknown!.ActualValue).IsEqualTo((SearchType)999);

            stack.Server.On("GET", "/v4/loadtracks", _ => new FakeRestResponse(
                200,
                """{"loadType":"empty","data":[],"pluginInfo":{}}"""
            ));
            var plain = await stack
                .Node.LoadTracksAsync("no position marker in this one")
                .ConfigureAwait(false);
            await Assert.That(plain.LoadType).IsEqualTo(LoadType.Empty);
        }
    }

    [Test]
    public async Task RemoveNyraNull()
    {
        await Assert
            .That(async () => await ((DiscordClient)null!).RemoveNyraAsync().ConfigureAwait(false))
            .Throws<ArgumentNullException>()
            .WithParameterName("client");
    }

    [Test]
    public async Task MissingGroupNamedSessionReturnsNull()
    {
        var stack = await StartAsync().ConfigureAwait(false);
        await using (stack.ConfigureAwait(false))
        {
            stack.Server.On(
                "GET",
                "/v4/sessions/fake-session/groups/session",
                _ => new FakeRestResponse(404, """{"message":"Group 'session' not found."}""")
            );
            await Assert
                .That(await stack.Node.Groups.GetAsync("session").ConfigureAwait(false))
                .IsNull();
        }
    }
}
