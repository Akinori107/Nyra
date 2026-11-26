using Nyra.Entities;
using Nyra.Queue;

namespace Nyra.EventArgs;

/// <summary>A <see cref="GuildQueue"/> skipped a track that could not play.</summary>
public sealed class QueueTrackFailedEventArgs : System.EventArgs
{
    /// <summary>The track that could not play.</summary>
    public Track Track { get; }

    /// <summary>Initializes the event args with the reported values.</summary>
    /// <param name="track">The track that could not play.</param>
    /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
    public QueueTrackFailedEventArgs(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        Track = track;
    }
}
