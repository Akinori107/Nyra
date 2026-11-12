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

    /// <summary>
    /// How long a guild connection can stay without <see cref="GuildConnection.Listeners"/> before it leaves its
    /// voice channel, off by default.
    /// </summary>
    /// <remarks>
    /// Counted from the moment no user who is not a bot is in the channel, someone joining stops it. When the
    /// time runs out the connection raises <see cref="GuildConnection.AloneTimedOut"/> and then disconnects.
    /// Needs a bridge that reports listeners, which the built-in bridges do, see
    /// <see cref="Abstractions.IVoiceListenerSource"/>. <see cref="Timeout.InfiniteTimeSpan"/>, the default,
    /// never leaves.
    /// </remarks>
    public TimeSpan AloneTimeout { get; init; } = Timeout.InfiniteTimeSpan;

    /// <summary>
    /// Whether a guild connection pauses when no <see cref="GuildConnection.Listeners"/> are left and resumes
    /// when someone joins again, off by default.
    /// </summary>
    /// <remarks>
    /// It only resumes a pause it made itself, a call to <see cref="GuildConnection.PauseAsync"/> or
    /// <see cref="GuildConnection.ResumeAsync"/> in between hands the pause back to the caller. Needs a bridge
    /// that reports listeners, the same as <see cref="AloneTimeout"/>.
    /// </remarks>
    public bool PauseWhenAlone { get; init; }

    /// <summary>How long the server keeps a disconnected resumable session alive.</summary>
    /// <remarks>
    /// Only used while <see cref="Resuming"/> is enabled. Sent in whole seconds.
    /// </remarks>
    public TimeSpan ResumeTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Delay before the first reconnect attempt, doubles with every failed attempt.</summary>
    /// <remarks>
    /// Each wait varies by up to 20% either way, then <see cref="MaxReconnectDelay"/> caps it. The delay is at
    /// least one millisecond.
    /// </remarks>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The longest a reconnect waits.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Consecutive reconnect attempts before giving up, negative means unlimited.</summary>
    public int MaxReconnectAttempts { get; init; } = -1;

    /// <summary>
    /// How long to wait for Discord's voice state and voice server updates before a handshake fails.
    /// </summary>
    /// <remarks>
    /// It also covers sending the join and leave updates to the gateway.
    /// </remarks>
    public TimeSpan VoiceHandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long the server may take to send ready after the websocket connected.</summary>
    /// <remarks>
    /// A node that takes longer is dropped and retried. The same time also limits the first REST check and the
    /// websocket connect, each on its own.
    /// </remarks>
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The clock the node and its connections read for elapsed time, timeouts and delays, the system clock by
    /// default.
    /// </summary>
    /// <remarks>Pass a fake one in tests to move time without waiting for it.</remarks>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
