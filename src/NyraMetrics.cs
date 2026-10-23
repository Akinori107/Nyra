using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Nyra.Entities;

namespace Nyra;

/// <summary>
/// Metrics of a Nyra process, published through the <c>System.Diagnostics.Metrics</c> meter <c>Nyra</c>.
/// </summary>
/// <remarks>
/// A listener sees nothing until it registers the meter. OpenTelemetry hosts call <c>AddMeter("Nyra")</c> on
/// their provider and <c>dotnet-counters</c> takes <c>--counters Nyra</c>.
/// <para>
/// Every gauge carries a <c>node</c> tag and an <c>endpoint</c> tag (<c>ws(s)://host:port</c>), one series per
/// node. The server-side gauges come from the node's <c>stats</c> reports, so a node that has not reported yet
/// only has its connectivity gauge.
/// </para>
/// </remarks>
public static class NyraMetrics
{
    /// <summary>The meter every Nyra instrument is published on.</summary>
    public static readonly Meter Meter = new("Nyra");

    private static readonly ConcurrentDictionary<Node, NodeSnapshot> Nodes = new();

    static NyraMetrics()
    {
        Meter.CreateObservableGauge(
            "playing-players",
            () => ObserveByNode(snapshot => snapshot.Stats?.PlayingPlayers),
            unit: "{players}",
            description: "Players with a track playing, from the node's stats report."
        );
        Meter.CreateObservableGauge(
            "not-playing-players",
            () =>
                ObserveByNode<int>(snapshot =>
                    snapshot.Stats is { } stats
                        ? Math.Max(0, stats.Players - stats.PlayingPlayers)
                        : null
                ),
            unit: "{players}",
            description: "Players without a track playing, from the node's stats report."
        );
        Meter.CreateObservableGauge(
            "players",
            () => ObserveByNode(snapshot => snapshot.Stats?.Players),
            unit: "{players}",
            description: "Players the node hosts, from the node's stats report."
        );
        Meter.CreateObservableGauge(
            "node-connected",
            () => ObserveByNode<int>(snapshot => snapshot.Connected ? 1 : 0),
            unit: "{connection}",
            description: "Whether the node's websocket is connected."
        );
        Meter.CreateObservableGauge(
            "node-uptime",
            () => ObserveByNode(snapshot => snapshot.Stats?.Uptime),
            unit: "ms",
            description: "Uptime of the node process, from the node's stats report."
        );
        Meter.CreateObservableGauge(
            "memory-used",
            () => ObserveByNode(snapshot => snapshot.Stats?.Memory.Used),
            unit: "By",
            description: "Memory used by the node process, from the node's stats report."
        );
        Meter.CreateObservableGauge(
            "memory-free",
            () => ObserveByNode(snapshot => snapshot.Stats?.Memory.Free),
            unit: "By",
            description: "Free memory of the node host on NodeLink, the JVM heap on Lavalink, from the node's stats report."
        );
        Meter.CreateObservableGauge(
            "cpu-system-load",
            () => ObserveByNode(snapshot => snapshot.Stats?.Cpu.SystemLoad),
            unit: "1",
            description: "System load of the node host, from the node's stats report."
        );
        Meter.CreateObservableGauge(
            "cpu-node-load",
            () => ObserveByNode(snapshot => snapshot.Stats?.Cpu.NodeLoad),
            unit: "1",
            description: "Load of the node process, structurally zero on a single-process NodeLink 3.9.x, watch cpu-system-load there, from the node's stats report."
        );
        Meter.CreateObservableGauge(
            "frames-sent",
            () => ObserveByNode(snapshot => snapshot.Stats?.FrameStats?.Sent),
            unit: "{frames}",
            description: "Audio frames sent, from the node's stats report."
        );
        Meter.CreateObservableGauge(
            "frames-nulled",
            () => ObserveByNode(snapshot => snapshot.Stats?.FrameStats?.Nulled),
            unit: "{frames}",
            description: "Audio frames sent as silence, from the node's stats report."
        );
        Meter.CreateObservableGauge(
            "frames-deficit",
            () => ObserveByNode(snapshot => snapshot.Stats?.FrameStats?.Deficit),
            unit: "{frames}",
            description: "Audio frames due but not sent, from the node's stats report."
        );
    }

    internal static void ReportStats(Node node, Stats stats) =>
        Nodes.GetOrAdd(node, NewSnapshot).Stats = stats;

    internal static void ReportConnection(Node node, bool connected) =>
        Nodes.GetOrAdd(node, NewSnapshot).Connected = connected;

    private static NodeSnapshot NewSnapshot(Node node) =>
        new()
        {
            Name = node.Name,
            Endpoint = DescribeEndpoint(node),
            Connected = node.IsConnected,
        };

    private static string DescribeEndpoint(Node node) =>
        $"ws{(node.Endpoint.IsSecure ? "s" : string.Empty)}://{node.Endpoint.Host}:{node.Endpoint.Port}";

    internal static void Forget(Node node) => Nodes.TryRemove(node, out _);

    private static IEnumerable<Measurement<T>> ObserveByNode<T>(Func<NodeSnapshot, T?> observe)
        where T : struct
    {
        var winners = new Dictionary<(string Name, string Endpoint), NodeSnapshot>();
        foreach (var (_, snapshot) in Nodes)
        {
            var key = (snapshot.Name, snapshot.Endpoint);
            if (
                !winners.TryGetValue(key, out var current)
                || (snapshot.Connected && !current.Connected)
            )
                winners[key] = snapshot;
        }

        foreach (var snapshot in winners.Values)
            if (observe(snapshot) is { } value)
                yield return new Measurement<T>(
                    value,
                    new KeyValuePair<string, object?>("node", snapshot.Name),
                    new KeyValuePair<string, object?>("endpoint", snapshot.Endpoint)
                );
    }

    private sealed class NodeSnapshot
    {
        public string Name { get; set; } = string.Empty;

        public string Endpoint { get; set; } = string.Empty;

        public Stats? Stats { get; set; }

        public bool Connected { get; set; }
    }
}
