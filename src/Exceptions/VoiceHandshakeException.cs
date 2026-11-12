namespace Nyra.Exceptions;

/// <summary>A Discord voice handshake that timed out, failed, or was cancelled by the Discord bridge.</summary>
/// <remarks>
/// Cancelling through your own token throws <see cref="OperationCanceledException"/> instead. Leaving the guild
/// during the join throws this while the handshake is still waiting, and
/// <see cref="InvalidOperationException"/> after it finished.
/// </remarks>
/// <param name="message">The message that describes the error.</param>
/// <param name="innerException">The exception that caused this one, null when there is none.</param>
public sealed class VoiceHandshakeException(string message, Exception? innerException = null)
    : NyraException(message, innerException) { }
