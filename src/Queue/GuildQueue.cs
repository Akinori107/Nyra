using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Nyra.Entities;
using Nyra.EventArgs;
using Nyra.Internal;

namespace Nyra.Queue;

/// <summary>The queue of one <see cref="GuildConnection"/>.</summary>
/// <remarks>
/// Get it with <see cref="GuildConnectionQueueExtensions.UseQueue"/>. The queue lives as long as the
/// connection does, a guild that leaves and joins again gets a new connection and so a new, empty queue. A guild
/// moved to another node by <see cref="NodeOptions.FailoverAfter"/> keeps this queue, unless the track could not
/// be sent to the new node, then it is cleared like after a restore. When the connection is invalidated the queue
/// is cleared and every call on it throws <see cref="InvalidOperationException"/>.
/// <para>
/// When the node loses its session and the connection is restored without the track being sent again, the new
/// player is empty, so the queue is cleared and its loop mode goes back to <see cref="LoopMode.Off"/>. A node
/// that resumes its session leaves the queue as it is.
/// </para>
/// <para>
/// Calls that change the queue run one at a time. Handlers of its events run in the order they were added, outside
/// that lock, so they may call the queue back, and an exception thrown by one goes to <see
/// cref="HandlerError"/> so it cannot break the queue.
/// </para>
/// </remarks>
public sealed class GuildQueue
{
    private static readonly ConditionalWeakTable<GuildConnection, GuildQueue> Queues = [];
    private static readonly Lock RegistryLock = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly NextTrackPreloader _preloader;
    private readonly Func<Exception, System.EventArgs?, Task> _handlerError;

    private volatile QueueState _state = QueueState.Empty;
    private volatile LoopMode _loop;
    private volatile bool _preloadNext;
    private volatile bool _detached;
    private volatile int _maxLength = int.MaxValue;
    private volatile int _historyLimit = 20;
    private int _expectingSuccessor;

    private GuildQueue(GuildConnection connection)
    {
        Connection = connection;
        _preloader = new NextTrackPreloader(this);
        _handlerError = OnHandlerErrorAsync;
        Subscribe(connection);
    }

    private void Subscribe(GuildConnection connection)
    {
        connection.TrackStarted += OnTrackStartedAsync;
        connection.TrackFinished += OnTrackFinishedAsync;
        connection.TrackStuck += OnTrackStuckAsync;
        connection.GuildConnectionRestored += OnRestoredAsync;
        connection.GuildConnectionInvalidated += OnInvalidatedAsync;
    }

    private void Unsubscribe(GuildConnection connection)
    {
        connection.TrackStarted -= OnTrackStartedAsync;
        connection.TrackFinished -= OnTrackFinishedAsync;
        connection.TrackStuck -= OnTrackStuckAsync;
        connection.GuildConnectionRestored -= OnRestoredAsync;
        connection.GuildConnectionInvalidated -= OnInvalidatedAsync;
    }

    /// <summary>Raised when a track becomes the current track.</summary>
    /// <remarks>
    /// Not raised again when <see cref="LoopMode.Track"/> repeats the same track.
    /// </remarks>
    public event AsyncEventHandler<QueueAdvancedEventArgs>? Advanced;

    /// <summary>Raised when the current track ended and nothing is left to play.</summary>
    /// <remarks>The loop mode goes back to <see cref="LoopMode.Off"/>.</remarks>
    public event AsyncEventHandler<QueueEndedEventArgs>? Ended;

    /// <summary>Raised when the track that plays after the current one changed.</summary>
    public event AsyncEventHandler<QueueNextChangedEventArgs>? NextChanged;

    /// <summary>Raised when a handler of another event of this queue threw.</summary>
    /// <remarks>
    /// The other handlers of that event still run. When nothing handles this event, or a handler of this event
    /// throws, the exception is dropped.
    /// </remarks>
    public event AsyncEventHandler<HandlerErrorEventArgs>? HandlerError;

    /// <summary>Raised when the queue skipped a track that could not play.</summary>
    /// <remarks>
    /// Covers a track that failed to load and a track that got stuck. A track played straight on the
    /// connection is not the queue's, so it does not raise this.
    /// </remarks>
    public event AsyncEventHandler<QueueTrackFailedEventArgs>? TrackFailed;

    /// <summary>Gets the connection this queue plays on.</summary>
    /// <remarks>It changes when the guild moves to another node, see <see cref="NodeOptions.FailoverAfter"/>.</remarks>
    public GuildConnection Connection { get; private set; }

    internal QueueState State => _state;

    /// <summary>Gets the track the queue is playing, null when it is idle.</summary>
    public Track? Current => _state.Current;

    /// <summary>Gets the tracks that play after the current one, in order.</summary>
    public IReadOnlyList<Track> Upcoming => _state.Upcoming;

    /// <summary>Gets the tracks that played before the current one, oldest first.</summary>
    /// <remarks>It keeps up to <see cref="HistoryLimit"/> tracks.</remarks>
    public IReadOnlyList<Track> History => _state.History;

    /// <summary>Gets the length of the upcoming tracks added together.</summary>
    /// <remarks>Streams are left out, they have no length.</remarks>
    public TimeSpan TotalDuration
    {
        get
        {
            var total = TimeSpan.Zero;
            foreach (var track in _state.Upcoming)
                if (!track.Info.IsStream)
                    total += track.Info.Length;

            return total;
        }
    }

    /// <summary>Gets how long it takes for an upcoming track to start playing.</summary>
    /// <remarks>Streams are left out, they have no length.</remarks>
    /// <param name="index">The position of the track in <see cref="Upcoming"/>, 0 plays next.</param>
    /// <returns>
    /// The time until the track starts, null when there is no track at the index, when
    /// <see cref="LoopMode.Track"/> repeats the current track, so no track after it starts, or when
    /// <see cref="Shuffle"/> is on and the index is past the next track, whose order is not known yet.
    /// </returns>
    public TimeSpan? TimeUntil(int index)
    {
        var state = _state;
        if (
            _loop == LoopMode.Track
            || index < 0
            || index >= state.Upcoming.Length
            || (Shuffle && index > 0)
        )
            return null;

        var wait = TimeSpan.Zero;
        if (state.Current is { Info.IsStream: false } current)
        {
            var left = current.Info.Length - (Connection.Position ?? TimeSpan.Zero);
            if (left > TimeSpan.Zero)
                wait = left;
        }

        for (var before = 0; before < index; before++)
            if (!state.Upcoming[before].Info.IsStream)
                wait += state.Upcoming[before].Info.Length;

        return wait;
    }

    /// <summary>Gets or sets how many upcoming tracks the queue holds, with no limit by default.</summary>
    /// <remarks>
    /// An enqueue that would go past the limit throws <see cref="InvalidOperationException"/> and adds nothing.
    /// Lowering it does not remove tracks that are already queued.
    /// </remarks>
    public int MaxLength
    {
        get => _maxLength;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _maxLength = value;
        }
    }

    /// <summary>Gets or sets how many played tracks <see cref="History"/> keeps, 20 by default.</summary>
    /// <remarks>Zero keeps none. A lower value takes effect as tracks end, it does not trim the history.</remarks>
    public int HistoryLimit
    {
        get => _historyLimit;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _historyLimit = value;
        }
    }

    /// <summary>Gets or sets what the queue repeats when a track ends.</summary>
    public LoopMode Loop
    {
        get => _loop;
        set
        {
            _loop = value;
            _ = _preloader.RefreshAsync();
        }
    }

    /// <summary>Gets or sets whether the next track is preloaded on the server, NodeLink only.</summary>
    /// <remarks>
    /// Turn it on for gapless playback, or for a crossfade once <see cref="GuildEffects.SetCrossfadeAsync"/>
    /// enabled it. It has no effect on a Lavalink node, the choice is made per connection. A failed preload
    /// only costs that transition its overlap, the next track then starts as usual.
    /// <para>
    /// Changing the first upcoming track while a crossfade is already being prepared makes NodeLink buffer
    /// again, so avoid reordering the queue in the last seconds of a track.
    /// </para>
    /// </remarks>
    public bool PreloadNext
    {
        get => _preloadNext;
        set
        {
            _preloadNext = value;
            _ = _preloader.RefreshAsync();
        }
    }

    /// <summary>Gets or sets whether each next track is picked at random from the queue, off by default.</summary>
    /// <remarks>
    /// The next track is picked ahead, so it is always the first of <see cref="Upcoming"/> and the rest keep
    /// their order. Unlike <see cref="ShuffleAsync"/>, tracks added later are picked too. Turning it on keeps the
    /// track that is already next. A track enqueued as next or moved to the top plays next, and moving the next
    /// track away picks a new one. It stays on when the queue stops or ends.
    /// </remarks>
    public bool Shuffle { get; set; }

    /// <summary>Gets or sets whether the queue keeps playing recommendations when it runs out, off by default.</summary>
    /// <remarks>
    /// When the last track ends, the queue loads recommendations for it and plays the first one that is not that
    /// track or in <see cref="History"/>. YouTube tracks use <c>ytrec</c> on NodeLink and the YouTube mix on
    /// Lavalink, Spotify tracks use <c>sprec</c>, which needs NodeLink or Lavalink with the LavaSrc plugin. A track
    /// of another source ends the queue as before. <see cref="Ended"/> is raised only when nothing was found, and
    /// a load that throws goes to <see cref="Node.Error"/>.
    /// </remarks>
    public bool Autoplay { get; set; }

    internal static GuildQueue For(GuildConnection connection)
    {
        lock (RegistryLock)
        {
            if (Queues.TryGetValue(connection, out var existing))
                return existing;

            var queue = new GuildQueue(connection);
            Queues.Add(connection, queue);
            return queue;
        }
    }

    internal static GuildQueue? Find(GuildConnection connection)
    {
        lock (RegistryLock)
            return Queues.TryGetValue(connection, out var queue) ? queue : null;
    }

    internal static async Task<(GuildConnection Connection, bool Resent)?> HandOverAsync(
        GuildConnection source,
        Func<Task<(GuildConnection Connection, bool Resent)?>> handOver
    )
    {
        if (Find(source) is not { } queue)
            return await handOver().ConfigureAwait(false);

        (GuildConnection Connection, bool Resent)? moved;
        var notes = new QueueNotes();
        await queue._gate.WaitAsync().ConfigureAwait(false);
        try
        {
            moved = await handOver().ConfigureAwait(false);
            if (moved is { } handedOver && !queue._detached)
            {
                queue.Unsubscribe(source);
                lock (RegistryLock)
                {
                    Queues.Remove(source);
                    Queues.AddOrUpdate(handedOver.Connection, queue);
                }

                queue.Connection = handedOver.Connection;
                queue.Subscribe(handedOver.Connection);
                if (handedOver.Resent)
                {
                    Interlocked.Exchange(ref queue._expectingSuccessor, 0);
                    queue._preloader.Forget();
                }
                else
                    queue.Reset(QueueState.Empty, notes);
            }
        }
        finally
        {
            queue._gate.Release();
        }

        if (moved is not null)
            await queue.FlushAsync(notes).ConfigureAwait(false);
        return moved;
    }

    /// <summary>Adds a track to the queue, and starts playing when nothing is playing.</summary>
    /// <param name="track">The track to add, the queue keeps its own copy.</param>
    /// <param name="next">Whether to add it at the front, so it plays after the current track.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The track that started playing, null when it was only queued.</returns>
    /// <exception cref="InvalidOperationException">The queue would go past <see cref="MaxLength"/>.</exception>
    public Task<Track?> EnqueueAsync(
        Track track,
        bool next = false,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(track);
        return EnqueueAsync([track], next, cancellationToken);
    }

    /// <summary>Adds tracks to the queue, and starts playing when nothing is playing.</summary>
    /// <param name="tracks">The tracks to add, in order, the queue keeps its own copies.</param>
    /// <param name="next">Whether to add them at the front, so they play after the current track.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The track that started playing, null when they were only queued.</returns>
    /// <exception cref="InvalidOperationException">The queue would go past <see cref="MaxLength"/>.</exception>
    public async Task<Track?> EnqueueAsync(
        IEnumerable<Track> tracks,
        bool next = false,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(tracks);

        List<Track> copies = [];
        foreach (var track in tracks)
        {
            ArgumentNullException.ThrowIfNull(track);
            copies.Add(track with { });
        }

        if (copies.Count == 0)
            return null;

        var notes = new QueueNotes();
        Track? started = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAttached();

            var idle = Connection.CurrentTrack is null;
            if (_state.Upcoming.Length + copies.Count - (idle ? 1 : 0) > _maxLength)
                throw new InvalidOperationException(
                    $"The queue holds at most {_maxLength} upcoming tracks."
                );

            var previous = _state;
            var queued = previous.Enqueue(copies, next, Policy);
            Commit(queued, notes);

            if (idle)
            {
                var (after, play) = queued.Start(Policy);
                if (play is not null)
                {
                    try
                    {
                        await _preloader
                            .PlayAsync(play, after, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        _state = previous;
                        throw;
                    }

                    Commit(after, notes);
                    notes.Advanced = started = play;
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        await FlushAsync(notes).ConfigureAwait(false);
        return started;
    }

    /// <summary>Plays an upcoming track now, the tracks before it stay in the queue.</summary>
    /// <remarks>A skip ignores <see cref="LoopMode.Track"/>, the current track does not repeat.</remarks>
    /// <param name="position">The position in <see cref="Upcoming"/>, starting at one, 1 plays the next track.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The track that started playing, null when the queue has no track at that position.</returns>
    public Task<Track?> SkipAsync(int position = 1, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, 1);
        return PlayChangeAsync(state => state.Skip(position, Policy), cancellationToken);
    }

    /// <summary>Goes back to the track that played before the current one.</summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The track that started playing, null when there is no history.</returns>
    public Task<Track?> PreviousAsync(CancellationToken cancellationToken = default) =>
        PlayChangeAsync(state => state.Previous(), cancellationToken);

    /// <summary>Removes an upcoming track.</summary>
    /// <param name="index">The position in <see cref="Upcoming"/>, starting at zero.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The removed track, null when the position does not exist.</returns>
    public Task<Track?> RemoveAsync(int index, CancellationToken cancellationToken = default) =>
        ChangeAsync(state => state.Remove(index, Policy), cancellationToken);

    /// <summary>Removes every upcoming track that matches.</summary>
    /// <remarks>
    /// The match runs on the tracks as they were when the call started, outside the queue's lock, so it may
    /// call the queue.
    /// </remarks>
    /// <param name="match">Decides which tracks to remove.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>How many tracks were removed.</returns>
    public async Task<int> RemoveAllAsync(
        Predicate<Track> match,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(match);

        var doomed = new HashSet<Track>(ReferenceEqualityComparer.Instance);
        foreach (var track in _state.Upcoming)
            if (match(track))
                doomed.Add(track);

        if (doomed.Count == 0)
            return 0;

        return await ChangeAsync(state => state.RemoveAll(doomed, Policy), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Moves an upcoming track to another position.</summary>
    /// <param name="from">The position of the track in <see cref="Upcoming"/>, starting at zero.</param>
    /// <param name="to">The position it should have afterwards, starting at zero.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>Whether both positions exist.</returns>
    public Task<bool> MoveAsync(int from, int to, CancellationToken cancellationToken = default) =>
        ChangeAsync(state => state.Move(from, to, Policy), cancellationToken);

    /// <summary>Removes every upcoming track, the current track keeps playing.</summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>A task that completes when the queue was cleared.</returns>
    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        ChangeAsync(state => (state.Clear(), true), cancellationToken);

    /// <summary>
    /// Shuffles the upcoming tracks once. <see cref="Shuffle"/> also shuffles the tracks added later.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>A task that completes when the queue was shuffled.</returns>
    public Task ShuffleAsync(CancellationToken cancellationToken = default) =>
        ChangeAsync(state => (state.Shuffled(Random.Shared), true), cancellationToken);

    /// <summary>Stops playback and clears the queue and the loop mode, the history is kept.</summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>A task that completes when the player was stopped.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var notes = new QueueNotes();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAttached();

            Reset(QueueState.Empty with { History = _state.History }, notes);
            await Connection.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await FlushAsync(notes).ConfigureAwait(false);
    }

    private async Task<Track?> PlayChangeAsync(
        Func<QueueState, (QueueState State, Track? Play)> change,
        CancellationToken cancellationToken
    )
    {
        var notes = new QueueNotes();
        Track? started;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAttached();

            var (after, play) = change(_state);
            if (play is null)
                return null;

            await _preloader.PlayAsync(play, after, cancellationToken).ConfigureAwait(false);
            Commit(after, notes);
            notes.Advanced = started = play;
        }
        finally
        {
            _gate.Release();
        }

        await FlushAsync(notes).ConfigureAwait(false);
        return started;
    }

    private async Task<TResult> ChangeAsync<TResult>(
        Func<QueueState, (QueueState State, TResult Result)> change,
        CancellationToken cancellationToken
    )
    {
        var notes = new QueueNotes();
        TResult result;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureAttached();

            var (after, value) = change(_state);
            Commit(after, notes);
            result = value;
        }
        finally
        {
            _gate.Release();
        }

        await FlushAsync(notes).ConfigureAwait(false);
        return result;
    }

    private void EnsureAttached()
    {
        if (_detached)
            throw new InvalidOperationException(
                "The guild connection of this queue was invalidated, use the queue of the new connection."
            );
    }

    private void Commit(QueueState after, QueueNotes notes)
    {
        var before = _state;
        _state = after;
        if (!ReferenceEquals(before.Head, after.Head))
            notes.NextChanged = true;
    }

    private void Reset(QueueState after, QueueNotes notes)
    {
        Commit(after, notes);
        _loop = LoopMode.Off;
        Interlocked.Exchange(ref _expectingSuccessor, 0);
        _preloader.Forget();
    }

    private async Task FlushAsync(QueueNotes notes)
    {
        var preload = _preloader.RefreshAsync();

        if (notes.Failed is { } failed)
            await TrackFailed
                .RaiseAsync(this, new QueueTrackFailedEventArgs(failed), _handlerError)
                .ConfigureAwait(false);

        if (notes.Advanced is { } advanced)
            await Advanced
                .RaiseAsync(this, new QueueAdvancedEventArgs(advanced), _handlerError)
                .ConfigureAwait(false);

        if (notes.Ended)
            await Ended
                .RaiseAsync(this, new QueueEndedEventArgs(), _handlerError)
                .ConfigureAwait(false);

        if (notes.NextChanged)
            await NextChanged
                .RaiseAsync(this, new QueueNextChangedEventArgs(_state.Head), _handlerError)
                .ConfigureAwait(false);

        await preload.ConfigureAwait(false);
    }

    private Task OnHandlerErrorAsync(Exception exception, System.EventArgs? origin) =>
        HandlerError.RaiseAsync(this, new HandlerErrorEventArgs(exception, origin), static (_, _) =>
            Task.CompletedTask
        );

    internal QueuePolicy Policy => new(_loop, _historyLimit, Shuffle ? Random.Shared : null);

    private bool Matches(QueueState state, Track? ended) =>
        state.Current is { } current
        && (
            ended is null
                ? Connection.CurrentTrack is null
                : GuildConnection.IsSameTrack(current, ended)
        );

    private Task OnTrackFinishedAsync(object? sender, TrackFinishedEventArgs args) =>
        args.EndReason switch
        {
            TrackEndReason.Finished => AdvanceAsync(args.Track, failed: false),
            TrackEndReason.LoadFailed => AdvanceAsync(args.Track, failed: true),
            TrackEndReason.Gapless or TrackEndReason.Crossfading => ServerAdvancedAsync(args.Track),
            _ => Task.CompletedTask,
        };

    private async Task AdvanceAsync(Track? ended, bool failed)
    {
        var notes = new QueueNotes();
        Exception? failure = null;
        Track? seed = null;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_detached)
                return;

            var before = _state;
            var ours = Matches(before, ended);
            if (!ours && (Connection.CurrentTrack is not null || before.Upcoming.IsEmpty))
                return;

            var (after, play) = ours ? before.Advance(failed, Policy) : before.Start(Policy);
            if (ours && failed)
                notes.Failed = before.Current;

            if (play is null)
            {
                Commit(after, notes);
                _loop = LoopMode.Off;
                if (Autoplay && (before.Current ?? ended) is { } last)
                    seed = last;
                else
                    notes.Ended = true;
            }
            else
            {
                try
                {
                    await _preloader
                        .PlayAsync(play, after, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Commit(before.Retire(ours && failed, Policy), notes);
                    failure = exception;
                }

                if (failure is null)
                {
                    Commit(after, notes);
                    if (!ReferenceEquals(play, before.Current))
                        notes.Advanced = play;
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        await FlushAsync(notes).ConfigureAwait(false);
        if (seed is not null && !await TryAutoplayAsync(seed).ConfigureAwait(false))
            await Ended
                .RaiseAsync(this, new QueueEndedEventArgs(), _handlerError)
                .ConfigureAwait(false);

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private async Task<bool> TryAutoplayAsync(Track seed)
    {
        if (RecommendationQuery(seed) is not { } query)
            return false;

        Track? next;
        try
        {
            var found = TracksOf(
                await Connection.Node.LoadTracksAsync(query, SearchType.Raw).ConfigureAwait(false)
            );
            var history = _state.History;
            next = found.FirstOrDefault(track =>
                !GuildConnection.IsSameTrack(track, seed)
                && !history.Any(played => GuildConnection.IsSameTrack(played, track))
            );
            if (next is null || _detached)
                return false;
            if (_state.Current is not null)
                return true;

            await EnqueueAsync(next).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Connection.Node.HandleError(exception);
            return false;
        }

        return _state.Current is not null;
    }

    private static IReadOnlyList<Track> TracksOf(LoadResult result) =>
        result switch
        {
            PlaylistLoaded playlist => playlist.Tracks,
            SearchLoaded search => search.Tracks,
            TrackLoaded loaded => [loaded.Track],
            _ => [],
        };

    private string? RecommendationQuery(Track seed)
    {
        var id = seed.Info.Identifier;
        if (string.IsNullOrEmpty(id))
            return null;

        return seed.Info.SourceName switch
        {
            "youtube" => Connection.Node.IsNodeLink
                ? $"ytrec:{id}"
                : $"https://www.youtube.com/watch?v={id}&list=RD{id}",
            "spotify" => $"sprec:seed_tracks={id}",
            _ => null,
        };
    }

    private async Task ServerAdvancedAsync(Track? ended)
    {
        var notes = new QueueNotes();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (
                _detached
                || _state.Current is not { } current
                || (ended is not null && !GuildConnection.IsSameTrack(current, ended))
            )
                return;

            Commit(_state.ServerAdvanced(Policy), notes);
            Interlocked.Exchange(ref _expectingSuccessor, 1);
            _preloader.Forget();
            notes.Advanced = _state.Current;
        }
        finally
        {
            _gate.Release();
        }

        await FlushAsync(notes).ConfigureAwait(false);
    }

    private async Task OnTrackStartedAsync(object? sender, TrackStartedEventArgs args)
    {
        var notes = new QueueNotes();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_detached && Interlocked.Exchange(ref _expectingSuccessor, 0) == 1)
            {
                var before = _state;
                var after = before.Reconcile(args.Track);
                if (!ReferenceEquals(before, after))
                {
                    Commit(after, notes);
                    notes.Advanced = after.Current;
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        _preloader.OnTrackStarted(args.Track);
        await FlushAsync(notes).ConfigureAwait(false);
    }

    private async Task OnTrackStuckAsync(object? sender, TrackStuckEventArgs args)
    {
        if (args.Track is { } stuck && !GuildConnection.IsSameTrack(_state.Current, stuck))
            return;

        if (_state.Current is { } failed)
            await TrackFailed
                .RaiseAsync(this, new QueueTrackFailedEventArgs(failed), _handlerError)
                .ConfigureAwait(false);

        if (await SkipAsync().ConfigureAwait(false) is null)
            await StopAsync().ConfigureAwait(false);
    }

    private async Task OnRestoredAsync(object? sender, GuildConnectionRestoredEventArgs args)
    {
        if (args.PlaybackResent)
            return;

        var notes = new QueueNotes();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_detached)
                return;

            Reset(QueueState.Empty, notes);
        }
        finally
        {
            _gate.Release();
        }

        await FlushAsync(notes).ConfigureAwait(false);
    }

    private async Task OnInvalidatedAsync(object? sender, GuildConnectionInvalidatedEventArgs args)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _detached = true;
            _state = QueueState.Empty;
            Unsubscribe(Connection);
            lock (RegistryLock)
                Queues.Remove(Connection);
        }
        finally
        {
            _gate.Release();
        }
    }
}
