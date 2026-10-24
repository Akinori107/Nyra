namespace Nyra;

/// <summary>Behavioral options of a <see cref="Node"/>.</summary>
public sealed record NodeOptions
{
    /// <summary>The node's display name, used in logs and error messages, must not be blank.</summary>
    public string Name { get; init; } = "Nyra node";

    /// <summary>When enabled, the session is marked resumable and the session id is replayed on reconnect.</summary>
    public bool Resuming { get; init; }

    /// <summary>How long the server keeps a disconnected resumable session alive.</summary>
    /// <remarks>
    /// Only used while <see cref="Resuming"/> is enabled, with resuming disabled the server drops the session
    /// on disconnect and this value is never read past validation. The wire form is whole seconds, a fraction
    /// is truncated.
    /// </remarks>
    public TimeSpan ResumeTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Delay before the first reconnect attempt, doubles with every failed attempt.</summary>
    /// <remarks>
    /// Every wait is jittered by ±20% before the cap applies, so the actual wait may be up to 20% shorter or
    /// longer than the configured value. With MaxReconnectDelay set equal to this value the wait never exceeds
    /// the configuration and can run 20% below it.
    /// <para>
    /// A sub-millisecond value would degenerate the backoff into a zero-delay retry loop, so the delay is
    /// floored at one millisecond.
    /// </para>
    /// </remarks>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Upper bound of the reconnect delay, the wait is jittered first and then capped here, so a reconnecting
    /// client never waits longer than this bound, except that every wait is floored at one millisecond.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Consecutive reconnect attempts before giving up, negative means unlimited.</summary>
    public int MaxReconnectAttempts { get; init; } = -1;

    /// <summary>
    /// How long to wait for Discord's voice state and voice server updates before a handshake fails.
    /// </summary>
    /// <remarks>
    /// The same bound also covers the gateway sends of the join and leave voice updates (a stuck gateway fails
    /// the handshake even while the updates are still pending). It also sizes the window in which a late echo
    /// of a compensatory leave is absorbed, so a longer timeout tolerates later echoes.
    /// </remarks>
    public TimeSpan VoiceHandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long the server may take to send ready after the websocket connected.</summary>
    /// <remarks>
    /// A node that takes longer is torn down and retried by the reconnect loop, so an edge proxy that
    /// completes the upgrade against a dead origin fails the connect instead of leaving it (and every node
    /// after it in a serial ConnectAllNodesAsync) waiting forever on a perfectly healthy socket.
    /// <para>
    /// The same budget also bounds the pre-connect REST probe (GET /v4/info) and the websocket upgrade itself
    /// (TCP, TLS and the HTTP upgrade handshake), each phase with its own full share. Tightening it for fast
    /// detection of a dead node also tightens the upgrade on a slow link.
    /// </para>
    /// </remarks>
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(15);
}
