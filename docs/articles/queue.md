# Queue

`UseQueue()` gets the queue of a guild connection and creates it on the first call:

```csharp
using Nyra.Queue;

var queue = connection.UseQueue();

await queue.EnqueueAsync(tracks);
await queue.SkipAsync();
await queue.ShuffleAsync();
queue.Loop = LoopMode.Queue;
```

`EnqueueAsync` plays the first track right away when nothing is playing. Positions in `Upcoming` start at zero, and `SkipAsync(3)` plays the third upcoming track and the two before it stay in the queue.

`ShuffleAsync` shuffles the queue once. With `Shuffle` on, every next track is picked at random, tracks added later included:

```csharp
queue.Shuffle = true;
```

The next track is picked ahead, so `Upcoming[0]` is always what plays next and what `PreloadNext` sends. The rest keep their order, and `TimeUntil` only knows the next track.

- `RemoveAllAsync(track => ...)` removes every upcoming track that matches.
- `TotalDuration` is the length of the upcoming tracks, streams not counted.
- `TimeUntil(index)` is the time left on the current track plus the tracks before `index`.
- `MaxLength` caps the upcoming tracks, an enqueue past it throws and adds nothing.
- `HistoryLimit` is how many played tracks `PreviousAsync` can go back to.

Events: `Advanced` when a new track starts, `Ended` when the last track ended and the loop mode went back to `Off`, `NextChanged` when the track after the current one changed and `TrackFailed` when a track was skipped because it could not play. An exception in your handler goes to `HandlerError` and does not stop the queue.

`Current`, `Upcoming` and `History` hold the same `Track` instances, so to remember who asked for a track, use them as the keys of your own map, like a `ConditionalWeakTable<Track, ...>`.

## What it follows

The queue moves on by itself when a track finishes or fails to load, not when you stop or replace it. A stuck track is skipped. A track you play with `connection.PlayAsync` is not in the queue, and the queue plays its next track when it ends.

A node that resumes its session leaves the queue as it is. When it loses the session, the connection is restored with an empty player. If `NodeOptions.RescuePlayback` sent the track again the queue goes on, and if `PlaybackResent` is false the queue is cleared and its loop mode goes back to `Off`.

The queue belongs to the guild connection. A guild that leaves and joins again, or moves to another node, gets a new connection and a new, empty queue. When a connection is invalidated its queue is cleared and every call on it throws.

## Gapless and crossfade

NodeLink can start the next track by itself, without a gap or with an overlap, but it has to know what comes next. `PreloadNext` tells it:

```csharp
queue.PreloadNext = true;
```

That gives a gapless change. For an overlap, enable the crossfade with `GuildEffects.SetCrossfadeAsync`, NodeLink only, see [Lavalink and NodeLink](servers.md).

- A queue on a Lavalink node ignores `PreloadNext`.
- Avoid reordering the queue in the last seconds of a track, NodeLink buffers the next track again.
- A crossfade needs both tracks to have a known length and not be streams, otherwise the change is gapless or plain.

## Autoplay

With `Autoplay` on, the queue doesn't stop when it runs out. It loads recommendations for the last track and plays the first one that isn't that track or in the history:

```csharp
queue.Autoplay = true;
```

- YouTube tracks use `ytrec` on NodeLink and the YouTube mix on Lavalink.
- Spotify tracks use `sprec`, which needs NodeLink or Lavalink with the LavaSrc plugin.
- A track of another source ends the queue as before.
- `Ended` is raised only when nothing was found.
