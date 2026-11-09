using Nyra.Entities;
using Nyra.Entities.Filters;
using Nyra.EventArgs;

namespace Nyra.Internal;

internal sealed class GuildPlayback
{
    private readonly GuildConnection _connection;

    private long _positionMilliseconds;
    private long _positionTick;
    private bool _positionAdvancing;
    private bool _hasPosition;
    private long _lastCaptureTime;

    private long _lastTrackEndTick;
    private Track? _lastEndedTrack;

    private long _lastSoughtTick;

    private bool _endedSinceAnchor;

    private bool _successorPending;

    private long _lastPauseEventTick;
    private long _lastVolumeEventTick;

    private long _lastPlayRequestTick;
    private long _lastPauseRequestTick;
    private long _lastResumeRequestTick;

    private long _requestSequence;
    private long _lastPlayRequestSequence;
    private long _lastPauseRequestSequence;
    private long _lastResumeRequestSequence;
    private Track? _anchorTrack;

    private readonly TimeProvider _time;

    internal GuildPlayback(GuildConnection connection, TimeProvider time)
    {
        _connection = connection;
        _time = time;
    }

    internal enum RequestKind
    {
        Play,
        Pause,
        Resume,
    }

    internal readonly record struct RequestTicks(
        RequestKind Kind,
        long Stamped,
        long Previous,
        long Sequence
    );

    internal RequestTicks StampPlayRequested() =>
        Stamp(RequestKind.Play, ref _lastPlayRequestTick, out _lastPlayRequestSequence);

    internal RequestTicks StampPauseRequested() =>
        Stamp(RequestKind.Pause, ref _lastPauseRequestTick, out _lastPauseRequestSequence);

    internal RequestTicks StampResumeRequested() =>
        Stamp(RequestKind.Resume, ref _lastResumeRequestTick, out _lastResumeRequestSequence);

    internal void RestoreRequestTicks(RequestTicks stamped)
    {
        lock (PositionLock)
        {
            ref var tick = ref _lastPlayRequestTick;
            ref var ownerSequence = ref _lastPlayRequestSequence;
            switch (stamped.Kind)
            {
                case RequestKind.Pause:
                    tick = ref _lastPauseRequestTick;
                    ownerSequence = ref _lastPauseRequestSequence;
                    break;
                case RequestKind.Resume:
                    tick = ref _lastResumeRequestTick;
                    ownerSequence = ref _lastResumeRequestSequence;
                    break;
            }

            if (ownerSequence == stamped.Sequence)
            {
                tick = stamped.Previous;
                ownerSequence = 0;
            }
        }
    }

    private RequestTicks Stamp(RequestKind kind, ref long tick, out long ownerSequence)
    {
        lock (PositionLock)
        {
            var stamp = new RequestTicks(kind, _time.GetMilliseconds(), tick, ++_requestSequence);
            tick = stamp.Stamped;
            ownerSequence = stamp.Sequence;
            return stamp;
        }
    }

    internal Lock PositionLock { get; } = new();

    internal TimeSpan? Position
    {
        get
        {
            lock (PositionLock)
            {
                if (!_hasPosition)
                    return null;

                return TrackInfo.ToTimeSpan(
                    _positionAdvancing
                        ? ProjectedPosition(_connection.Filters.Applied.Timescale)
                        : _positionMilliseconds
                );
            }
        }
    }

    private long ProjectedPosition(TimescaleFilter? timescale)
    {
        var length = _connection.CurrentTrack?.Info.LengthMilliseconds ?? 0;
        var headroom =
            length > 0 ? length - _positionMilliseconds : TrackInfo.MaxTimeSpanMilliseconds;
        var position =
            _positionMilliseconds + PaceDelta(_positionTick, PaceFactor(timescale), headroom);

        return length > 0 && position > length ? length : position;
    }

    private void Anchor(long positionMilliseconds, Track? track, long? tick = null)
    {
        _positionMilliseconds = positionMilliseconds;
        _positionTick = tick ?? _time.GetMilliseconds();
        _hasPosition = true;
        _anchorTrack = track;
        _endedSinceAnchor = false;
    }

    private static double PaceFactor(TimescaleFilter? timescale)
    {
        var speed = (timescale?.Speed ?? 1.0) * (timescale?.Rate ?? 1.0);
        return double.IsFinite(speed) ? Math.Max(0.0, speed) : 1.0;
    }

    private long PaceDelta(long fromTick, double pace, long ceiling)
    {
        const double maxLong = 9.2233720368547747E18;
        var delta = (_time.GetMilliseconds() - fromTick) * pace;
        var capped = Math.Min(delta, ceiling);
        return capped >= maxLong ? long.MaxValue - 1 : (long)Math.Round(capped);
    }

    internal void OnTrackStart(TrackStartedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(eventArgs.Track.Encoded))
            return;

        bool pauseHoldsOverStart;
        lock (PositionLock)
        {
            pauseHoldsOverStart =
                !_connection.IsNodeLinkServer
                && _lastPauseRequestSequence > _lastPlayRequestSequence
                && _lastPauseRequestSequence > _lastResumeRequestSequence;

            if (!pauseHoldsOverStart)
                _lastPauseEventTick = _time.GetMilliseconds();
        }

        if (!pauseHoldsOverStart)
            _connection.IsPaused = false;

        lock (PositionLock)
        {
            _connection.CurrentTrack = eventArgs.Track;

            if (
                _hasPosition
                && (
                    !GuildConnection.IsSameTrack(_anchorTrack, eventArgs.Track)
                    || (
                        _endedSinceAnchor
                        && GuildConnection.IsSameTrack(_lastEndedTrack, eventArgs.Track)
                    )
                )
            )
            {
                _positionMilliseconds = 0;
                _positionTick = _time.GetMilliseconds();
            }

            _endedSinceAnchor = false;

            _anchorTrack = eventArgs.Track;
            _positionAdvancing =
                (_connection.State?.Connected ?? true)
                && !_connection.IsPaused
                && eventArgs.Track is { Info.IsStream: false };
            _successorPending = false;
        }
    }

    internal void OnTrackEnd(TrackFinishedEventArgs eventArgs)
    {
        var isTransition =
            eventArgs.EndReason
            is TrackEndReason.Replaced
                or TrackEndReason.Gapless
                or TrackEndReason.Crossfading;

        lock (PositionLock)
        {
            if (eventArgs.Track is { } emptyTrack && string.IsNullOrEmpty(emptyTrack.Encoded))
                eventArgs.Track = null;

            var previous = _connection.CurrentTrack;
            var matchesCurrent = eventArgs.Track is not null
                ? GuildConnection.IsSameTrack(previous, eventArgs.Track)
                : _successorPending || _endedSinceAnchor;

            var admitted = false;
            if (isTransition)
            {
                if (matchesCurrent)
                {
                    _successorPending = true;
                    admitted = true;
                }
            }
            else
            {
                if (matchesCurrent || _successorPending)
                {
                    admitted = true;
                    FreezePosition();
                    _connection.CurrentTrack = null;
                    _positionAdvancing = false;
                }

                _successorPending = false;
            }

            var ended = eventArgs.Track ?? (admitted ? previous : null);
            if (ended is not null)
            {
                _lastTrackEndTick = _time.GetMilliseconds();
                _lastEndedTrack = ended;
            }

            if (admitted)
                _endedSinceAnchor = true;
        }
    }

    internal void OnPlayerUpdate(PlayerUpdateEventArgs eventArgs)
    {
        if (!eventArgs.StateCarried)
            return;

        lock (PositionLock)
        {
            if (_connection.CurrentTrack is null)
            {
                _connection.State = eventArgs.State;
                return;
            }

            if (eventArgs.State.TimeMilliseconds < _lastCaptureTime)
                return;

            _connection.State = eventArgs.State;
            Anchor(eventArgs.State.PositionMilliseconds, _connection.CurrentTrack);
            _positionAdvancing =
                eventArgs.State.Connected
                && !_connection.IsPaused
                && _connection.CurrentTrack is { Info.IsStream: false };
            if (eventArgs.State.TimeMilliseconds > _lastCaptureTime)
                _lastCaptureTime = eventArgs.State.TimeMilliseconds;
        }
    }

    internal void OnTrackSought(TrackSoughtEventArgs eventArgs)
    {
        lock (PositionLock)
        {
            if (_connection.CurrentTrack is null)
                return;
            _lastSoughtTick = _time.GetMilliseconds();
            Anchor(
                eventArgs.PositionMilliseconds is >= 0 and <= TrackInfo.MaxTimeSpanMilliseconds
                    ? eventArgs.PositionMilliseconds
                    : 0,
                _connection.CurrentTrack
            );
        }
    }

    internal void OnPauseChanged(PauseChangedEventArgs eventArgs)
    {
        lock (PositionLock)
        {
            _lastPauseEventTick = _time.GetMilliseconds();
            _connection.IsPaused = eventArgs.IsPaused;
            if (eventArgs.IsPaused)
                FreezePosition();
            else
            {
                _positionTick = _time.GetMilliseconds();
                _positionAdvancing =
                    (_connection.State?.Connected ?? true)
                    && _connection.CurrentTrack is { Info.IsStream: false };
            }
        }
    }

    internal void OnVolumeChanged(VolumeChangedEventArgs eventArgs)
    {
        lock (PositionLock)
            _lastVolumeEventTick = _time.GetMilliseconds();

        _connection.Volume = eventArgs.Volume;
    }

    internal void Capture(Player player, long requestTick, long? startMilliseconds = null)
    {
        lock (PositionLock)
        {
            if (_connection.IsTearingDown)
                return;

            if (player.State is { } ranked && ranked.TimeMilliseconds < _lastCaptureTime)
                return;

            if (_lastPauseEventTick < requestTick || _lastPauseRequestTick >= _lastPauseEventTick)
                _connection.IsPaused = player.IsPaused;
            if (_lastVolumeEventTick < requestTick)
                _connection.Volume = player.Volume;

            if (
                player.Track is not null
                && _lastTrackEndTick >= requestTick
                && GuildConnection.IsSameTrack(_lastEndedTrack, player.Track)
            )
                return;

            var holdsPreSeekPosition = _lastSoughtTick >= requestTick;

            _connection.CurrentTrack = player.Track;
            _successorPending = false;

            if (player.State is { } state)
            {
                if (player.Track is not null && !holdsPreSeekPosition)
                {
                    _connection.State = state;
                    _lastCaptureTime = state.TimeMilliseconds;
                    if (startMilliseconds is { } start)
                        Anchor(start, player.Track, requestTick);
                    else
                        Anchor(state.PositionMilliseconds, player.Track);
                }
                else
                    _connection.State = state;
            }

            _positionAdvancing =
                (player.State?.Connected ?? true)
                && !_connection.IsPaused
                && player.Track is { Info.IsStream: false };
        }
    }

    internal void OnTimescaleChangedUnderLock(TimescaleFilter? oldTimescale)
    {
        if (!_positionAdvancing)
            return;

        _positionMilliseconds = ProjectedPosition(oldTimescale);
        _positionTick = _time.GetMilliseconds();
    }

    private void FreezePosition()
    {
        lock (PositionLock)
        {
            if (!_positionAdvancing)
                return;

            _positionMilliseconds = ProjectedPosition(_connection.Filters.Applied.Timescale);
            _positionTick = _time.GetMilliseconds();
            _positionAdvancing = false;
        }
    }

    internal void Clear()
    {
        lock (PositionLock)
        {
            _connection.CurrentTrack = null;
            _lastCaptureTime = 0;
            _positionAdvancing = false;
            _hasPosition = false;
            _anchorTrack = null;
            _lastEndedTrack = null;
            _successorPending = false;
            _endedSinceAnchor = false;
            _lastTrackEndTick = 0;
            _lastSoughtTick = 0;
        }
    }
}
