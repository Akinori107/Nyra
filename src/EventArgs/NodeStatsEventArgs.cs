using Nyra.Entities;

namespace Nyra.EventArgs;

/// <summary>The statistics a node reported.</summary>
public sealed class NodeStatsEventArgs : System.EventArgs
{
    /// <summary>The reported statistics; also the node's current <see cref="Node.Stats"/>.</summary>
    public Stats Stats { get; }

    public NodeStatsEventArgs(Stats stats)
    {
        Stats = stats;
    }
}
