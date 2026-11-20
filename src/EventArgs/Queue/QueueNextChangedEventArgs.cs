using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The track that plays after the current one changed.</summary>
/// <param name="next">The track that plays next, null when the queue has nothing after the current track.</param>
public sealed class QueueNextChangedEventArgs(Track? next) : System.EventArgs
{
    /// <summary>The track that plays next, null when the queue has nothing after the current track.</summary>
    public Track? Next { get; } = next;
}
