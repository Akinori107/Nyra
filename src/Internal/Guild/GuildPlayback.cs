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

    private readonly Lock _positionLock = new();

    private Track? _anchorTrack;

    internal GuildPlayback(GuildConnection connection) => _connection = connection;

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
        lock (_positionLock)
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
        lock (_positionLock)
        {
            var stamp = new RequestTicks(kind, Environment.TickCount64, tick, ++_requestSequence);
            tick = stamp.Stamped;
            ownerSequence = stamp.Sequence;
            return stamp;
        }
    }

    internal Lock PositionLock => _positionLock;

    internal TimeSpan? Position
    {
        get
        {
            lock (_positionLock)
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

    private void Anchor(long positionMilliseconds, Track? track)
    {
        _positionMilliseconds = positionMilliseconds;
        _positionTick = Environment.TickCount64;
        _hasPosition = true;
        _anchorTrack = track;
        _endedSinceAnchor = false;
    }

    private static double PaceFactor(TimescaleFilter? timescale)
    {
        var speed = (timescale?.Speed ?? 1.0) * (timescale?.Rate ?? 1.0);
        return double.IsFinite(speed) ? Math.Max(0.0, speed) : 1.0;
    }

    private static long PaceDelta(long fromTick, double pace, long ceiling)
    {
        const double maxLong = 9.2233720368547747E18;
        var delta = (Environment.TickCount64 - fromTick) * pace;
        var capped = Math.Min(delta, ceiling);
        return capped >= maxLong ? long.MaxValue - 1 : (long)Math.Round(capped);
    }

    internal void OnTrackStart(TrackStartedEventArgs eventArgs)
    {
        if (string.IsNullOrEmpty(eventArgs.Track.Encoded))
            return;

        bool pauseHoldsOverStart;
        lock (_positionLock)
        {
            pauseHoldsOverStart =
                !_connection.IsNodeLinkServer
                && _lastPauseRequestSequence > _lastPlayRequestSequence
                && _lastPauseRequestSequence > _lastResumeRequestSequence;

            if (!pauseHoldsOverStart)
                _lastPauseEventTick = Environment.TickCount64;
        }

        if (!pauseHoldsOverStart)
            _connection.IsPaused = false;

        lock (_positionLock)
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
                _positionTick = Environment.TickCount64;
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

        lock (_positionLock)
        {
            if (eventArgs.Track is { } emptyTrack && string.IsNullOrEmpty(emptyTrack.Encoded))
                eventArgs.Track = null;

            var matchesCurrent = eventArgs.Track is not null
                ? GuildConnection.IsSameTrack(_connection.CurrentTrack, eventArgs.Track)
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

            _lastTrackEndTick = Environment.TickCount64;
            _lastEndedTrack = eventArgs.Track;
            if (admitted)
                _endedSinceAnchor = true;
        }
    }

    internal void OnPlayerUpdate(PlayerUpdateEventArgs eventArgs)
    {
        if (!eventArgs.StateCarried)
            return;

        lock (_positionLock)
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
        lock (_positionLock)
        {
            if (_connection.CurrentTrack is null)
                return;
            _lastSoughtTick = Environment.TickCount64;
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
        lock (_positionLock)
        {
            _lastPauseEventTick = Environment.TickCount64;
            _connection.IsPaused = eventArgs.IsPaused;
            if (eventArgs.IsPaused)
                FreezePosition();
            else
            {
                _positionTick = Environment.TickCount64;
                _positionAdvancing =
                    (_connection.State?.Connected ?? true)
                    && _connection.CurrentTrack is { Info.IsStream: false };
            }
        }
    }

    internal void OnVolumeChanged(VolumeChangedEventArgs eventArgs)
    {
        lock (_positionLock)
            _lastVolumeEventTick = Environment.TickCount64;

        _connection.Volume = eventArgs.Volume;
    }

    internal void Capture(Player player, long requestTick)
    {
        lock (_positionLock)
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
        _positionTick = Environment.TickCount64;
    }

    private void FreezePosition()
    {
        lock (_positionLock)
        {
            if (!_positionAdvancing)
                return;

            _positionMilliseconds = ProjectedPosition(_connection.Filters.Applied.Timescale);
            _positionTick = Environment.TickCount64;
            _positionAdvancing = false;
        }
    }

    internal void Clear()
    {
        lock (_positionLock)
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
