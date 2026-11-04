using Discord;
using Microsoft.Extensions.Logging;
using Nyra.Entities;
using Nyra.EventArgs;

namespace Nyra.Examples;

internal enum LoopMode
{
    Off,
    Track,
    Queue,
}

internal sealed class Queue
{
    private static readonly Dictionary<ulong, Queue> Queues = [];
    private static readonly Lock Gate = new();

    private readonly GuildConnection _connection;
    private readonly List<Track> _pending = [];
    private Track? _current;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _start = new(1, 1);

    private Queue(GuildConnection connection)
    {
        _connection = connection;
        connection.TrackStarted += OnTrackStartedAsync;
        connection.TrackFinished += OnTrackFinishedAsync;
        connection.TrackStuck += OnTrackStuckAsync;
        connection.TrackException += OnTrackExceptionAsync;
        connection.PlayerDestroyed += OnPlayerDestroyedAsync;
        connection.GuildConnectionInvalidated += OnConnectionInvalidatedAsync;
    }

    public static ILogger? Logger { get; set; }

    public LoopMode Loop { get; set; }

    public bool Crossfade { get; set; }

    public IMessageChannel? Announce { get; set; }

    public static Queue For(GuildConnection connection)
    {
        lock (Gate)
        {
            if (Queues.TryGetValue(connection.GuildId, out var existing))
            {
                if (existing._connection == connection)
                    return existing;

                existing.Detach();
            }

            var queue = new Queue(connection);
            Queues[connection.GuildId] = queue;
            return queue;
        }
    }

    public IReadOnlyList<Track> Snapshot()
    {
        lock (_lock)
            return [.. _pending];
    }

    public void Add(IEnumerable<Track> tracks)
    {
        lock (_lock)
            _pending.AddRange(tracks);
    }

    public void Reset()
    {
        lock (_lock)
        {
            _pending.Clear();
            Loop = LoopMode.Off;
        }
    }

    public async Task<Track?> StartIfIdleAsync()
    {
        await _start.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_connection.CurrentTrack is not null)
                return null;

            Track? next;
            lock (_lock)
                next = Dequeue();

            if (next is not null)
                await PlayAsync(next).ConfigureAwait(false);

            return next;
        }
        finally
        {
            _start.Release();
        }
    }

    public async Task<Track?> SkipAsync(int position = 1)
    {
        Track? next;
        lock (_lock)
        {
            if (position > 1 && position > _pending.Count)
                return null;

            Requeue(_current);
            next = Dequeue(position - 1);
        }

        if (next is not null)
            await PlayAsync(next).ConfigureAwait(false);

        return next;
    }

    public async Task PreloadNextAsync()
    {
        if (!Crossfade)
            return;

        Track? head;
        lock (_lock)
            head = Loop == LoopMode.Track || _pending.Count == 0 ? null : _pending[0];

        if (head is not null)
            await _connection.Effects.SetNextTrackAsync(head).ConfigureAwait(false);
    }

    private Task PlayAsync(Track track)
    {
        _current = track;
        return _connection.PlayAsync(track);
    }

    private void Requeue(Track? track)
    {
        if (Loop == LoopMode.Queue && track is not null)
            _pending.Add(track);
    }

    private Track? Dequeue(int index = 0)
    {
        if (index >= _pending.Count)
            return null;

        var next = _pending[index];
        _pending.RemoveAt(index);
        return next;
    }

    private async Task OnTrackFinishedAsync(object? sender, TrackFinishedEventArgs args)
    {
        var finished = _current;
        Track? next = null;
        Track? announced = null;

        lock (_lock)
        {
            switch (args.EndReason)
            {
                case TrackEndReason.Finished when Loop == LoopMode.Track && finished is not null:
                    next = finished;
                    break;

                case TrackEndReason.Finished
                or TrackEndReason.LoadFailed:
                    if (args.EndReason == TrackEndReason.Finished)
                        Requeue(finished);

                    next = announced = Dequeue();
                    if (next is null)
                        Loop = LoopMode.Off;

                    break;

                case TrackEndReason.Gapless
                or TrackEndReason.Crossfading:
                    Requeue(finished);
                    announced = Dequeue();
                    _current = announced ?? _current;
                    break;
            }
        }

        if (next is not null)
            await PlayAsync(next).ConfigureAwait(false);

        if (announced is not null)
            await AnnounceAsync(announced).ConfigureAwait(false);
    }

    private Task OnTrackExceptionAsync(object? sender, TrackExceptionEventArgs args)
    {
        Logger?.LogWarning(
            "Couldn't play {Track}: {Reason}",
            args.Track?.Info.Title ?? "the track",
            args.Exception.Message
        );
        return Task.CompletedTask;
    }

    private async Task OnTrackStuckAsync(object? sender, TrackStuckEventArgs args)
    {
        if (await SkipAsync().ConfigureAwait(false) is { } next)
            await AnnounceAsync(next).ConfigureAwait(false);
        else
        {
            Reset();
            await _connection.StopAsync().ConfigureAwait(false);
        }
    }

    private Task OnTrackStartedAsync(object? sender, TrackStartedEventArgs args) =>
        PreloadNextAsync();

    private Task AnnounceAsync(Track track) => NotifyAsync($"Now playing: {track.Info.Title}");

    private async Task NotifyAsync(string message)
    {
        if (Announce is not { } channel)
            return;

        try
        {
            await channel.SendMessageAsync(message).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Logger?.LogWarning(exception, "Couldn't send a message to the channel");
        }
    }

    private Task OnPlayerDestroyedAsync(object? sender, PlayerDestroyedEventArgs args)
    {
        Remove();
        return Task.CompletedTask;
    }

    private Task OnConnectionInvalidatedAsync(
        object? sender,
        GuildConnectionInvalidatedEventArgs args
    )
    {
        Remove();
        return Task.CompletedTask;
    }

    private void Remove()
    {
        lock (Gate)
        {
            if (Queues.TryGetValue(_connection.GuildId, out var current) && current == this)
                Queues.Remove(_connection.GuildId);
        }

        Detach();
    }

    private void Detach()
    {
        _connection.TrackStarted -= OnTrackStartedAsync;
        _connection.TrackFinished -= OnTrackFinishedAsync;
        _connection.TrackStuck -= OnTrackStuckAsync;
        _connection.TrackException -= OnTrackExceptionAsync;
        _connection.PlayerDestroyed -= OnPlayerDestroyedAsync;
        _connection.GuildConnectionInvalidated -= OnConnectionInvalidatedAsync;
    }
}
