using System.Collections.Immutable;
using Nyra.Entities;

namespace Nyra.Queue;

internal readonly record struct QueuePolicy(
    LoopMode Loop,
    int HistoryLimit,
    Random? Shuffle = null
);

internal sealed record QueueState(
    Track? Current,
    ImmutableArray<Track> Upcoming,
    ImmutableArray<Track> History
)
{
    internal static QueueState Empty { get; } = new(null, [], []);

    internal Track? Head => Upcoming.IsEmpty ? null : Upcoming[0];

    internal Track? NextToPreload(QueuePolicy policy) =>
        policy.Loop == LoopMode.Track ? null : Head;

    internal QueueState Enqueue(IReadOnlyList<Track> tracks, bool next, QueuePolicy policy)
    {
        if (next)
            return this with { Upcoming = Upcoming.InsertRange(0, tracks) };

        var after = this with { Upcoming = Upcoming.AddRange(tracks) };
        return Upcoming.IsEmpty ? after.Pick(policy) : after;
    }

    internal (QueueState State, Track? Play) Start(QueuePolicy policy) =>
        Retire(failed: false, policy).Take(policy);

    internal (QueueState State, Track? Play) Advance(bool failed, QueuePolicy policy)
    {
        if (Current is null)
            return (this, null);

        if (!failed && policy.Loop == LoopMode.Track)
            return (this, Current);

        return Retire(failed, policy).Take(policy);
    }

    internal (QueueState State, Track? Play) Skip(int position, QueuePolicy policy)
    {
        if (position < 1 || position > Upcoming.Length)
            return (this, null);

        var target = Upcoming[position - 1];
        var rest = this with { Upcoming = Upcoming.RemoveAt(position - 1) };
        var after = rest.Retire(failed: false, policy) with { Current = target };
        return (position == 1 ? after.Pick(policy) : after, target);
    }

    internal (QueueState State, Track? Play) Previous()
    {
        if (History.IsEmpty)
            return (this, null);

        var previous = History[^1];
        return (
            this with
            {
                Current = previous,
                History = History.RemoveAt(History.Length - 1),
                Upcoming = Current is { } current ? Upcoming.Insert(0, current) : Upcoming,
            },
            previous
        );
    }

    internal QueueState ServerAdvanced(QueuePolicy policy) =>
        Retire(failed: false, policy).Take(policy).State;

    internal QueueState Reconcile(Track started)
    {
        if (
            GuildConnection.IsSameTrack(Current, started)
            || (
                string.IsNullOrEmpty(started.Info.Identifier)
                && string.IsNullOrEmpty(started.Encoded)
            )
        )
            return this;

        var index = IndexOf(Upcoming, started);
        var upcoming = index >= 0 ? Upcoming.RemoveAt(index) : Upcoming;
        if (Current is { } expected)
            upcoming = upcoming.Insert(0, expected);

        return this with
        {
            Current = index >= 0 ? Upcoming[index] : started,
            Upcoming = upcoming,
        };
    }

    internal (QueueState State, Track? Removed) Remove(int index, QueuePolicy policy)
    {
        if (index < 0 || index >= Upcoming.Length)
            return (this, null);

        var after = this with { Upcoming = Upcoming.RemoveAt(index) };
        return (index == 0 ? after.Pick(policy) : after, Upcoming[index]);
    }

    internal (QueueState State, int Removed) RemoveAll(HashSet<Track> doomed, QueuePolicy policy)
    {
        var kept = Upcoming.RemoveAll(doomed.Contains);
        var after = this with { Upcoming = kept };
        return (
            Head is { } head && doomed.Contains(head) ? after.Pick(policy) : after,
            Upcoming.Length - kept.Length
        );
    }

    internal (QueueState State, bool Moved) Move(int from, int to, QueuePolicy policy)
    {
        if (from < 0 || from >= Upcoming.Length || to < 0 || to >= Upcoming.Length)
            return (this, false);

        if (from == to)
            return (this, true);

        var track = Upcoming[from];
        var after = this with { Upcoming = Upcoming.RemoveAt(from).Insert(to, track) };
        return (from == 0 ? after.Pick(policy) : after, true);
    }

    internal QueueState Clear() => this with { Upcoming = [] };

    internal QueueState Shuffled(Random random)
    {
        if (Upcoming.Length < 2)
            return this;

        var items = Upcoming.ToBuilder();
        for (var index = items.Count - 1; index > 0; index--)
        {
            var other = random.Next(index + 1);
            (items[index], items[other]) = (items[other], items[index]);
        }

        return this with
        {
            Upcoming = items.ToImmutable(),
        };
    }

    private (QueueState State, Track? Play) Take(QueuePolicy policy) =>
        Upcoming.IsEmpty
            ? (this, null)
            : (
                (this with { Current = Upcoming[0], Upcoming = Upcoming.RemoveAt(0) }).Pick(policy),
                Upcoming[0]
            );

    private QueueState Pick(QueuePolicy policy)
    {
        if (policy.Shuffle is not { } random || Upcoming.Length < 2)
            return this;

        var picked = random.Next(Upcoming.Length);
        if (picked == 0)
            return this;

        return this with
        {
            Upcoming = Upcoming.RemoveAt(picked).Insert(0, Upcoming[picked]),
        };
    }

    internal QueueState Retire(bool failed, QueuePolicy policy)
    {
        if (Current is not { } current)
            return this;

        if (failed)
            return this with { Current = null };

        return this with
        {
            Current = null,
            History = Remember(current, policy),
            Upcoming = policy.Loop == LoopMode.Queue ? Upcoming.Add(current) : Upcoming,
        };
    }

    private ImmutableArray<Track> Remember(Track track, QueuePolicy policy)
    {
        var history = History.Add(track);
        return history.Length > policy.HistoryLimit
            ? history.RemoveRange(0, history.Length - policy.HistoryLimit)
            : history;
    }

    private static int IndexOf(ImmutableArray<Track> tracks, Track track)
    {
        for (var index = 0; index < tracks.Length; index++)
            if (GuildConnection.IsSameTrack(tracks[index], track))
                return index;

        return -1;
    }
}
