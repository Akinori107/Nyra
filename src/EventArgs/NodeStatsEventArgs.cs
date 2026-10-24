using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The statistics a node reported.</summary>
public sealed class NodeStatsEventArgs : System.EventArgs
{
    /// <summary>
    /// The reported statistics, the same snapshot <see cref="Node.Stats"/> held when the raise was queued.
    /// </summary>
    /// <remarks>
    /// By handler time a newer report may have replaced the property. A report that lands while the ready
    /// phase runs is raised only after the Ready event, or dropped without a raise if the connection dies
    /// first. The property already holds its data meanwhile, so the last raised snapshot can trail the
    /// property until the deferred raise runs.
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
