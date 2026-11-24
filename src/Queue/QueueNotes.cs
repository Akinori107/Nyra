using Nyra.Entities;
using Nyra.EventArgs;

namespace Nyra.Queue;

internal sealed class QueueNotes
{
    internal Track? Failed { get; set; }

    internal Track? Advanced { get; set; }

    internal bool Ended { get; set; }

    internal QueueNextChangedEventArgs? NextChanged { get; set; }
}
