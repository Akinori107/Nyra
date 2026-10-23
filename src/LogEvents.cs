using Microsoft.Extensions.Logging;

namespace Nyra;

/// <summary>Log event ids used by Nyra.</summary>
/// <remarks>
/// Messages the library forwards from third-party libraries (a Discord client's own log output) are re-emitted
/// without an id from this catalog.
/// </remarks>
public static class LogEvents
{
    /// <summary>The event id of the gateway intents a bridge requires.</summary>
    /// <remarks>
    /// Named NyraIntents to stay distinct from a Discord library's own "Intents" event id in the same logger
    /// pipeline, a filter silencing theirs must not silence this critical with it.
    /// </remarks>
    public static EventId Intents { get; } = new(1000, "NyraIntents");

    /// <summary>The event id of node activity.</summary>
    public static EventId Node { get; } = new(1001, "Node");

    /// <summary>The event id of shard routing.</summary>
    public static EventId Sharding { get; } = new(1002, "Sharding");

    /// <summary>The event id of per-guild playback activity.</summary>
    public static EventId Playback { get; } = new(1003, "Playback");

    /// <summary>The event id of the extension's own activity.</summary>
    public static EventId Extension { get; } = new(1004, "Extension");

    /// <summary>The event id of the Discord bridge adapters.</summary>
    public static EventId Bridge { get; } = new(1005, "Bridge");
}
