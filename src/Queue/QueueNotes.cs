using Nyra.Entities;

namespace Nyra.Queue;

internal sealed class QueueNotes
{
    internal Track? Failed { get; set; }

    internal Track? Advanced { get; set; }

    internal bool Ended { get; set; }

    internal bool NextChanged { get; set; }
}
