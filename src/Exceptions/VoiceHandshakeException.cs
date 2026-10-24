namespace Nyra.Exceptions;

/// <summary>A Discord voice handshake that timed out, failed, or was cancelled by the Discord bridge.</summary>
/// <remarks>
/// A caller cancellation surfaces as <see cref="OperationCanceledException"/> instead. The inner exception
/// of a timeout is the raw bound exception, an <see cref="OperationCanceledException"/> for the send bound,
/// a <see cref="TimeoutException"/> for the update bound.
/// <para>
/// A guild removal during the join arrives as this type only while the handshake was still waiting, once
/// the updates completed the registry's backstop raises an <see cref="InvalidOperationException"/> for the
/// same cause instead.
/// </para>
/// </remarks>
public sealed class VoiceHandshakeException : NyraException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one, null when there is none.</param>
    public VoiceHandshakeException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
