namespace Nyra;

/// <summary>Behavioral options of a <see cref="Node"/>.</summary>
public sealed record NodeOptions
{
    /// <summary>The node's display name, used in logs and error messages; must not be blank.</summary>
    public string Name { get; init; } = "Nyra node";

    /// <summary>When enabled, the session is marked resumable and the session id is replayed on reconnect.</summary>
    public bool Resuming { get; init; }

    /// <summary>How long the server keeps a disconnected resumable session alive.</summary>
    public TimeSpan ResumeTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Delay before the first reconnect attempt; doubles with every failed attempt.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Upper bound of the exponential reconnect delay.</summary>
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Consecutive reconnect attempts before giving up; negative means unlimited.</summary>
    public int MaxReconnectAttempts { get; init; } = -1;

    /// <summary>How long to wait for Discord's voice state and voice server updates before a handshake fails.</summary>
    public TimeSpan VoiceHandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
