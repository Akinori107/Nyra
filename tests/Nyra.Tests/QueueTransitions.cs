using System.Text.Json;
using Nyra.Entities;
using Nyra.Queue;

namespace Nyra.Tests;

public sealed class QueueTransitions
{
    private static readonly QueuePolicy Off = new(LoopMode.Off, 20);

    private static Track Make(string id) =>
        JsonSerializer.Deserialize<Track>(
            $$$"""{"encoded":"enc-{{{id}}}","info":{"identifier":"{{{id}}}","author":"a","title":"{{{id}}}","length":100000,"isStream":false,"isSeekable":true,"position":0}}""",
            NyraJson.SerializerOptions
        )!;

    private static QueueState Playing(string current, params string[] upcoming) =>
        new(Make(current), [.. upcoming.Select(Make)], []);

    private static string Ids(IEnumerable<Track> tracks) =>
        string.Join(",", tracks.Select(track => track.Info.Identifier));

    [Test]
    public async Task StartTakesTheHead()
    {
        var (state, play) = QueueState
            .Empty.Enqueue([Make("a"), Make("b")], next: false)
            .Start(Off);

        await Assert.That(play?.Info.Identifier).IsEqualTo("a");
        await Assert.That(state.Current?.Info.Identifier).IsEqualTo("a");
        await Assert.That(Ids(state.Upcoming)).IsEqualTo("b");
    }

    [Test]
    public async Task EnqueueNextGoesToTheFront()
    {
        var state = Playing("a", "b").Enqueue([Make("c")], next: true);

        await Assert.That(Ids(state.Upcoming)).IsEqualTo("c,b");
    }

    [Test]
    public async Task AdvanceMovesTheCurrentToHistory()
    {
        var (state, play) = Playing("a", "b").Advance(failed: false, Off);

        await Assert.That(play?.Info.Identifier).IsEqualTo("b");
        await Assert.That(Ids(state.History)).IsEqualTo("a");
        await Assert.That(state.Upcoming.IsEmpty).IsTrue();
    }

    [Test]
    public async Task AdvanceOnTheLastTrackLeavesNothingPlaying()
    {
        var (state, play) = Playing("a").Advance(failed: false, Off);

        await Assert.That(play).IsNull();
        await Assert.That(state.Current).IsNull();
        await Assert.That(Ids(state.History)).IsEqualTo("a");
    }

    [Test]
    public async Task AdvanceAfterAFailureDoesNotRememberTheTrack()
    {
        var (state, play) = Playing("a", "b").Advance(failed: true, Off);

        await Assert.That(play?.Info.Identifier).IsEqualTo("b");
        await Assert.That(state.History.IsEmpty).IsTrue();
    }

    [Test]
    public async Task LoopTrackRepeatsTheCurrentTrack()
    {
        var before = Playing("a", "b");
        var (state, play) = before.Advance(failed: false, new QueuePolicy(LoopMode.Track, 20));

        await Assert.That(play?.Info.Identifier).IsEqualTo("a");
        await Assert.That(state).IsSameReferenceAs(before);
    }

    [Test]
    public async Task LoopTrackStillAdvancesAfterAFailure()
    {
        var (_, play) = Playing("a", "b")
            .Advance(failed: true, new QueuePolicy(LoopMode.Track, 20));

        await Assert.That(play?.Info.Identifier).IsEqualTo("b");
    }

    [Test]
    public async Task LoopQueueSendsTheFinishedTrackToTheEnd()
    {
        var (state, play) = Playing("a", "b")
            .Advance(failed: false, new QueuePolicy(LoopMode.Queue, 20));

        await Assert.That(play?.Info.Identifier).IsEqualTo("b");
        await Assert.That(Ids(state.Upcoming)).IsEqualTo("a");
    }

    [Test]
    public async Task SkipPlaysTheTrackAtThePositionAndKeepsTheOnesBefore()
    {
        var (state, play) = Playing("a", "b", "c", "d").Skip(2, Off);

        await Assert.That(play?.Info.Identifier).IsEqualTo("c");
        await Assert.That(Ids(state.History)).IsEqualTo("a");
        await Assert.That(Ids(state.Upcoming)).IsEqualTo("b,d");
    }

    [Test]
    public async Task SkipPastTheEndChangesNothing()
    {
        var before = Playing("a", "b");
        var (state, play) = before.Skip(2, Off);

        await Assert.That(play).IsNull();
        await Assert.That(state).IsSameReferenceAs(before);
    }

    [Test]
    public async Task SkipWithLoopQueueKeepsEveryTrack()
    {
        var (state, play) = Playing("a", "b", "c").Skip(2, new QueuePolicy(LoopMode.Queue, 20));

        await Assert.That(play?.Info.Identifier).IsEqualTo("c");
        await Assert.That(Ids(state.Upcoming)).IsEqualTo("b,a");
    }

    [Test]
    public async Task PreviousBringsBackTheLastTrackAndRequeuesTheCurrent()
    {
        var (afterSkip, _) = Playing("a", "b").Skip(1, Off);
        var (state, play) = afterSkip.Previous();

        await Assert.That(play?.Info.Identifier).IsEqualTo("a");
        await Assert.That(Ids(state.Upcoming)).IsEqualTo("b");
        await Assert.That(state.History.IsEmpty).IsTrue();
    }

    [Test]
    public async Task PreviousWithoutHistoryChangesNothing()
    {
        var before = Playing("a", "b");
        var (state, play) = before.Previous();

        await Assert.That(play).IsNull();
        await Assert.That(state).IsSameReferenceAs(before);
    }

    [Test]
    public async Task HistoryKeepsOnlyTheLimit()
    {
        var policy = new QueuePolicy(LoopMode.Off, 2);
        var (state, _) = Playing("a", "b", "c", "d").Skip(1, policy);
        (state, _) = state.Skip(1, policy);
        (state, _) = state.Skip(1, policy);

        await Assert.That(Ids(state.History)).IsEqualTo("b,c");
    }

    [Test]
    public async Task HistoryLimitZeroKeepsNothing()
    {
        var (state, _) = Playing("a", "b").Advance(failed: false, new QueuePolicy(LoopMode.Off, 0));

        await Assert.That(state.History.IsEmpty).IsTrue();
    }

    [Test]
    public async Task MoveReordersTheUpcomingTracks()
    {
        var (state, moved) = Playing("a", "b", "c", "d").Move(2, 0);

        await Assert.That(moved).IsTrue();
        await Assert.That(Ids(state.Upcoming)).IsEqualTo("d,b,c");
    }

    [Test]
    public async Task MoveOutOfRangeChangesNothing()
    {
        var before = Playing("a", "b", "c");
        var (state, moved) = before.Move(5, 0);

        await Assert.That(moved).IsFalse();
        await Assert.That(state).IsSameReferenceAs(before);
    }

    [Test]
    public async Task RemoveTakesOutOneTrack()
    {
        var (state, removed) = Playing("a", "b", "c").Remove(0);

        await Assert.That(removed?.Info.Identifier).IsEqualTo("b");
        await Assert.That(Ids(state.Upcoming)).IsEqualTo("c");
    }

    [Test]
    public async Task RemoveOutOfRangeReturnsNull()
    {
        var (_, removed) = Playing("a", "b").Remove(3);

        await Assert.That(removed).IsNull();
    }

    [Test]
    public async Task RemoveAllTakesOutTheGivenInstances()
    {
        var state = Playing("a", "b", "c", "d");
        var doomed = new HashSet<Track> { state.Upcoming[0], state.Upcoming[2] };
        var (after, removed) = state.RemoveAll(doomed);

        await Assert.That(removed).IsEqualTo(2);
        await Assert.That(Ids(after.Upcoming)).IsEqualTo("c");
    }

    [Test]
    public async Task ClearKeepsTheCurrentTrack()
    {
        var state = Playing("a", "b", "c").Clear();

        await Assert.That(state.Current?.Info.Identifier).IsEqualTo("a");
        await Assert.That(state.Upcoming.IsEmpty).IsTrue();
    }

    [Test]
    public async Task ShuffleKeepsTheSameTracks()
    {
        var before = Playing("a", "b", "c", "d", "e", "f");
        var after = before.Shuffled(new Random(7));

        await Assert.That(after.Upcoming.Length).IsEqualTo(5);
        await Assert
            .That(Ids(after.Upcoming.OrderBy(track => track.Info.Identifier)))
            .IsEqualTo("b,c,d,e,f");
        await Assert.That(after.Current).IsSameReferenceAs(before.Current);
    }

    [Test]
    public async Task ServerAdvanceMovesTheCursorWithoutPlaying()
    {
        var state = Playing("a", "b").ServerAdvanced(Off);

        await Assert.That(state.Current?.Info.Identifier).IsEqualTo("b");
        await Assert.That(Ids(state.History)).IsEqualTo("a");
    }

    [Test]
    public async Task ReconcileAdoptsAnUpcomingTrackThatStartedEarly()
    {
        var before = Playing("a", "b", "c");
        var after = before.Reconcile(Make("c"));

        await Assert.That(after.Current?.Info.Identifier).IsEqualTo("c");
        await Assert.That(Ids(after.Upcoming)).IsEqualTo("a,b");
    }

    [Test]
    public async Task ReconcileLeavesTheCurrentTrackAlone()
    {
        var before = Playing("a", "b");

        await Assert.That(before.Reconcile(Make("a"))).IsSameReferenceAs(before);
    }

    [Test]
    public async Task PreloadTargetIsTheHeadUnlessTheTrackLoops()
    {
        var state = Playing("a", "b");

        await Assert.That(state.NextToPreload(Off)?.Info.Identifier).IsEqualTo("b");
        await Assert.That(state.NextToPreload(new QueuePolicy(LoopMode.Track, 20))).IsNull();
    }

    [Test]
    public async Task AdvanceWithoutACurrentTrackChangesNothing()
    {
        var before = QueueState.Empty.Enqueue([Make("a")], next: false);
        var (state, play) = before.Advance(failed: false, Off);

        await Assert.That(play).IsNull();
        await Assert.That(state).IsSameReferenceAs(before);
    }

    [Test]
    public async Task MoveToTheSamePositionKeepsTheState()
    {
        var before = Playing("a", "b", "c");
        var (state, moved) = before.Move(1, 1);

        await Assert.That(moved).IsTrue();
        await Assert.That(state).IsSameReferenceAs(before);
    }

    [Test]
    public async Task ShuffleOfASingleTrackKeepsTheState()
    {
        var before = Playing("a", "b");

        await Assert.That(before.Shuffled(new Random(7))).IsSameReferenceAs(before);
    }

    [Test]
    public async Task ReconcileOfAnUnknownTrackRequeuesTheCurrent()
    {
        var after = Playing("a", "b").Reconcile(Make("z"));

        await Assert.That(after.Current?.Info.Identifier).IsEqualTo("z");
        await Assert.That(Ids(after.Upcoming)).IsEqualTo("a,b");
    }
}
