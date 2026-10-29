# Getting started

This uses DSharpPlus. The other libraries differ in a few lines, see [Adapters](adapters.md).

## Install

```sh
dotnet add package Nyra.DSharpPlus
```

## Start the bot

```csharp
using DSharpPlus;
using Nyra;
using Nyra.DSharpPlus;
using Nyra.Entities;

using var client = new DiscordClient(new DiscordConfiguration
{
    Token = token,
    TokenType = TokenType.Bot,
    Intents = DiscordIntents.AllUnprivileged,
});

var nyra = client.UseNyra();
nyra.AddNode(new Node(
    new Endpoint("localhost", 2333, "youshallnotpass"),
    new NodeOptions { Name = "main", Resuming = true }));

await client.ConnectAsync();
await nyra.ConnectAllNodesAsync();

// ... the bot runs ...

await client.DisconnectAsync();
await nyra.DisposeAsync();
```

`Endpoint` is the host, port and password of your server. In order:

1. `UseNyra()` has to run before the client connects.
2. `AddNode` only adds the node. `ConnectAllNodesAsync()` connects it, and it can run right after `ConnectAsync` because it waits for Discord to be ready.
3. `DisposeAsync()` closes the nodes when the bot stops.

## Play something

When a node is ready, `GetNode()` gives you one, and `ConnectAsync` joins a voice channel on it:

```csharp
var node = nyra.GetNode()!;
var connection = await node.ConnectAsync(guildId, voiceChannelId);

var result = await node.LoadTracksAsync("never gonna give you up");

var track = result switch
{
    LoadResult.TrackLoaded loaded => loaded.Track,
    LoadResult.SearchLoaded { Tracks: [var first, ..] } => first,
    LoadResult.PlaylistLoaded { Tracks: [var first, ..] } => first,
    _ => null,
};

if (track is not null)
    await connection.PlayAsync(track);
```

`LoadResult.LoadFailed` means the server failed, and `Error.Message` says why.

Later, in another command, `nyra.GetConnection(guildId)` gives the connection back. With several servers, `GetNode()` gives the one with the fewest playing players.

## Play the next track

Nyra has no queue. It tells you when a track ends, and you start the next one. Only some end reasons mean you should:

```csharp
connection.TrackFinished += async (sender, args) =>
{
    if (args.EndReason is not (TrackEndReason.Finished or TrackEndReason.LoadFailed))
        return;

    if (queue.TryDequeue(out var next))
        await connection.PlayAsync(next);
};
```

The other reasons are on [TrackEndReason](../api/Nyra.Entities.TrackEndReason.yml).

## What next

- [Adapters](adapters.md) has the setup of each Discord library.
- [When a connection breaks](connection.md) covers kicks, moves and a server going down.
- [Lavalink and NodeLink](servers.md) lists what each server can do.
- The [`examples`](https://github.com/Akinori107/Nyra/tree/dev/examples) folder has a slash command bot that uses all of this.
