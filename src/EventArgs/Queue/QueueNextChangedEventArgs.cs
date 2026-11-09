using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The track that plays after the current one changed.</summary>
public sealed class QueueNextChangedEventArgs : System.EventArgs
{
    /// <summary>The track that plays next, null when the queue has nothing after the current track.</summary>
    public Track? Next { get; }

    /// <summary>Initializes the event args with the reported values.</summary>
    /// <param name="next">The track that plays next, null when the queue has nothing after the current track.</param>
    public QueueNextChangedEventArgs(Track? next) => Next = next;
}
