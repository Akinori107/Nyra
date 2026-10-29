# When a connection breaks

## The bot is kicked or leaves the voice channel

If someone kicks the bot, disconnects it, or the bot is removed from the server, Nyra closes that guild's player on the server and drops the `GuildConnection`. No event is raised for this.

After that, `nyra.GetConnection(guildId)` returns `null`, and `connection.IsConnected` is `false`. Joining again gives you a **new** `GuildConnection`, so subscribe to its events again.

## The bot is moved

Nothing breaks. The connection stays, and `connection.ChannelId` changes to the new channel.

## The server drops

Nyra reconnects by itself, waiting longer after each failure. It never stops unless you set `MaxReconnectAttempts` in [NodeOptions](../api/Nyra.NodeOptions.yml).

With `Resuming = true`, the server keeps the players for `ResumeTimeout`, and audio continues when the node is back. `NodeReady` tells you what happened:

```csharp
nyra.NodeReady += (sender, args) =>
{
    Console.WriteLine(args.Resumed == true ? "Session resumed" : "New session");
    return Task.CompletedTask;
};
```

After a resume, `await connection.RefreshAsync()` updates a connection with what the server says. `await node.ListPlayersAsync()` lists every player of the session, including guilds this bot isn't connected to.

## The players are gone

If the session can't be resumed, or a NodeLink worker crashes, the players are lost. Each affected connection raises `GuildConnectionInvalidated`. `args.LastChannelId` is the channel it was in, so you can rejoin:

```csharp
connection.GuildConnectionInvalidated += async (sender, args) =>
{
    if (args.LastChannelId is not { } channelId || nyra.GetNode() is not { } node)
        return;

    var fresh = await node.ConnectAsync(args.GuildId, channelId);
    // fresh is a new connection, subscribe to its events here
};
```

## The node gives up

When `MaxReconnectAttempts` runs out, or the first connect fails for good, `NodeDisconnected` is raised with `Final = true`, and the node stops trying. `args.Exception` has the last error.

```csharp
nyra.NodeDisconnected += (sender, args) =>
{
    if (args.Final)
        Console.WriteLine($"{((Node)sender!).Name} gave up: {args.Exception?.Message}");
    return Task.CompletedTask;
};
```

Guild connections on that node stay registered until it connects again. Call `ConnectAllNodesAsync()` to try again.

## More info

Other events that help when something goes wrong are `nyra.NodeError`, `nyra.NodeWorkerFailed`, `connection.WebSocketClosed` and `connection.HandlerError`, and on NodeLink `PlayerReconnecting`, `PlayerConnected`, `PlayerCreated` and `PlayerDestroyed`. What each one means is on [GuildConnection](../api/Nyra.GuildConnection.yml) and [NyraExtension](../api/Nyra.NyraExtension.yml).
