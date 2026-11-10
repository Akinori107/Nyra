namespace Nyra;

/// <summary>Behavioral options of a <see cref="Node"/>.</summary>
public sealed record NodeOptions
{
    /// <summary>The node's display name, used in logs and error messages, must not be blank.</summary>
    public string Name { get; init; } = "Nyra node";

    /// <summary>
    /// When enabled, the session is marked resumable and the session id is replayed on reconnect, on by default.
    /// </summary>
    public bool Resuming { get; init; } = true;

    /// <summary>
    /// Whether to play again what was playing when the node comes back with a session it could not resume, off
    /// by default.
    /// </summary>
    /// <remarks>
    /// A last resort. When the connection is restored on the new session, Nyra sends the volume,
    /// the filters, the track from where it was when the node dropped, and the pause back to the node. The
    /// position is an estimate. What Nyra does not keep, like the end time, the audio track, the crossfade,
    /// the ducking and the mix, is not restored, and a queue of your own is not affected. <see
    /// cref="Nyra.EventArgs.GuildConnectionRestoredEventArgs.PlaybackResent"/> says whether the track was sent
    /// again.
    /// </remarks>
    public bool RescuePlayback { get; init; }

    /// <summary>
    /// How long the node can stay down for <see cref="RescuePlayback"/> to still play the track again, 3 minutes
    /// by default.
    /// </summary>
    /// <remarks>
    /// Counted from the moment the node dropped. After that the connection is restored without playing, the bot
    /// stays in its voice channel and <see
    /// cref="Nyra.EventArgs.GuildConnectionRestoredEventArgs.PlaybackResent"/> is false. Use <see
    /// cref="Timeout.InfiniteTimeSpan"/> for no limit. Only used while <see cref="RescuePlayback"/> is enabled.
    /// </remarks>
    public TimeSpan RescueTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How long a guild connection can go without a track before it leaves its voice channel, off by default.
    /// </summary>
    /// <remarks>
    /// Counted from the moment nothing is playing, a paused track still counts as playing. When the time runs out
    /// the connection raises <see cref="GuildConnection.IdleTimedOut"/> and then disconnects, unless a handler
    /// started a track. <see cref="Timeout.InfiniteTimeSpan"/>, the default, never leaves.
    /// </remarks>
    public TimeSpan IdleTimeout { get; init; } = Timeout.InfiniteTimeSpan;

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

    /// <summary>
    /// The clock the node and its connections read for elapsed time, timeouts and delays, the system clock by
    /// default.
    /// </summary>
    /// <remarks>Pass a fake one in tests to move time without waiting for it.</remarks>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
