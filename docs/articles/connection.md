# When a connection breaks

## The bot is kicked or leaves the voice channel

If someone kicks the bot, disconnects it, or the bot is removed from the server, Nyra closes that guild's player on the server and drops the `GuildConnection`. No event is raised for this.

After that, `nyra.GetConnection(guildId)` returns `null`, and `connection.IsConnected` is `false`. Joining again gives you a **new** `GuildConnection`, so subscribe to its events again.

## The bot is moved

Nothing breaks. The connection stays, and `connection.ChannelId` changes to the new channel.

## Nothing plays for a while

Set `IdleTimeout` in [NodeOptions](../api/Nyra.NodeOptions.yml) to leave the voice channel when nothing plays for that long. The count starts when the connection has no track, a paused track still counts as playing. When the time runs out the connection raises `IdleTimedOut` and then disconnects, unless a handler started a track. It is off by default.

```csharp
var node = new Node(endpoint, new NodeOptions { IdleTimeout = TimeSpan.FromMinutes(5) });

connection.IdleTimedOut += (sender, args) =>
{
    Console.WriteLine($"Left channel {args.ChannelId} of guild {args.GuildId}, nothing was playing.");
    return Task.CompletedTask;
};
```

## Nobody is listening

`connection.Listeners` counts the users in the voice channel who are not bots, and `ListenersChanged` is raised when it changes. The built-in bridges read it from the library's cache, a custom bridge reports it by implementing `IVoiceListenerSource`, without one it stays `null`.

Two options in [NodeOptions](../api/Nyra.NodeOptions.yml) act on it, both off by default:

```csharp
var node = new Node(endpoint, new NodeOptions
{
    AloneTimeout = TimeSpan.FromMinutes(2),
    PauseWhenAlone = true,
});
```

- `AloneTimeout` leaves the channel when nobody listened for that long. The connection raises `AloneTimedOut` and then disconnects, unless someone joined in the meantime.
- `PauseWhenAlone` pauses when the last listener leaves and resumes when someone joins. It only resumes its own pause, a `PauseAsync` or `ResumeAsync` in between leaves the pause to you.

## The server drops

Nyra reconnects by itself, waiting longer after each failure. It never stops unless you set `MaxReconnectAttempts` in [NodeOptions](../api/Nyra.NodeOptions.yml).

`Resuming` is on by default, so the server keeps the players for `ResumeTimeout`, and audio continues when the node is back. `NodeReady` tells you what happened:

```csharp
nyra.NodeReady += (sender, args) =>
{
    Console.WriteLine(args.Resumed == true ? "Session resumed" : "New session");
    return Task.CompletedTask;
};
```

After a resume, `await connection.RefreshAsync()` updates a connection with what the server says. `await node.ListPlayersAsync()` lists every player of the session, including guilds this bot isn't connected to.

## The players are gone

If the session can't be resumed, the new session has no players. Nyra sends it the voice details it kept, so the bot stays in the voice channel, and each connection raises `GuildConnectionRestored`. The player is empty, the track, the volume and the filters are gone, so tell your users to play again, or start playing yourself:

```csharp
connection.GuildConnectionRestored += (sender, args) =>
{
    // for example, send "The music server restarted, play again" to the guild
    return Task.CompletedTask;
};
```

If you can't lose what was playing, set `RescuePlayback = true` in [NodeOptions](../api/Nyra.NodeOptions.yml). Nyra then sends the track from where it was when the node dropped, the volume, the filters and the pause back to the node, and `args.PlaybackResent` says if the track was sent again. It is a last resort. If the node stays down for longer than `RescueTimeout` (3 minutes by default), Nyra only restores the voice and `PlaybackResent` is false. The position is an estimate, and what Nyra doesn't keep, like the end time, the audio track, the crossfade, the ducking and the mix, is not restored. A queue of your own keeps working. The [queue](queue.md) of Nyra goes on if `PlaybackResent` is true and is cleared if it is false.

If the restore fails, or a NodeLink worker crashes, the connection is lost instead. It raises `GuildConnectionInvalidated`. `args.LastChannelId` is the channel it was in, so you can rejoin:

```csharp
connection.GuildConnectionInvalidated += async (sender, args) =>
{
    if (args.LastChannelId is not { } channelId || nyra.GetNode() is not { } node)
        return;

    var fresh = await node.ConnectAsync(args.GuildId, channelId);
    // fresh is a new connection, subscribe to its events here
};
```

## Another node takes over

With more than one node, set `FailoverAfter` in [NodeOptions](../api/Nyra.NodeOptions.yml) and the guilds of a node that drops move to the best ready node of the same kind. The bot stays in the voice channel. The track goes on from where it was, with what `RescuePlayback` sends back even when it is off, and the [queue](queue.md) of Nyra follows the guild. `TimeSpan.Zero` moves as soon as the node drops, a longer time gives the node that long to come back:

```csharp
var node = new Node(endpoint, new NodeOptions { FailoverAfter = TimeSpan.Zero });

connection.GuildConnectionMigrated += (sender, args) =>
{
    var moved = args.Connection;
    // moved is the connection on args.To, subscribe to its events here
    return Task.CompletedTask;
};
```

Nyra can't tell a dead node from a lost websocket, so a node that was still playing loses its guilds too, with a short gap in the audio. The players it has when it comes back are destroyed. If no other node of the same kind is ready, the guilds wait and move once one is.

Guilds never move between NodeLink and Lavalink. A track NodeLink encoded may not decode on Lavalink, which answers `Could not decode the provided track`, so the guild would come back without its track, and the rest of its queue would fail the same way. The filters differ too, NodeLink has filters Lavalink lacks and accepts values Lavalink refuses, and the mix and the crossfade only exist on NodeLink. To fail over, run at least two nodes of the same kind.

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
