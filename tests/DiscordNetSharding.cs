using Discord.WebSocket;

namespace Nyra.Tests;

public sealed class DiscordNetSharding
{
    [Test]
    public async Task ShardedLookup()
    {
        using var sharded = new DiscordShardedClient();
        using var bridge = new DiscordNet.DiscordNetBridge(sharded);

        await Assert
            .That(async () => await bridge.SendVoiceUpdateAsync(123, 456).ConfigureAwait(false))
            .Throws<InvalidOperationException>();
    }
}
