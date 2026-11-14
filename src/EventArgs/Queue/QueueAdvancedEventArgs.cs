using Nyra.Entities;
using Nyra.Queue;

namespace Nyra.EventArgs;

/// <summary>A track became the current track of a <see cref="GuildQueue"/>.</summary>
public sealed class QueueAdvancedEventArgs : System.EventArgs
{
    /// <summary>The track that is now playing.</summary>
    public Track Track { get; }

    /// <summary>Initializes the event args with the reported values.</summary>
    /// <param name="track">The track that is now playing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
    public QueueAdvancedEventArgs(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        Track = track;
    }
}
