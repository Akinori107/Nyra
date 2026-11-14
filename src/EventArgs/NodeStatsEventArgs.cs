using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The statistics a node reported.</summary>
public sealed class NodeStatsEventArgs : System.EventArgs
{
    /// <summary>
    /// The reported statistics.
    /// </summary>
    /// <remarks>
    /// <see cref="Node.Stats"/> may already be newer. A report that arrives during the ready handshake is raised
    /// after <see cref="Node.Ready"/>.
    /// </remarks>
    public Stats Stats { get; }

    /// <summary>Initializes the event args with the reported values.</summary>
    /// <param name="stats">The statistics the node reported.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
    public NodeStatsEventArgs(Stats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        Stats = stats;
    }
}
