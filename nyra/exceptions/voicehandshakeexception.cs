namespace Nyra.Exceptions;

/// <summary>A Discord voice handshake that timed out, was cancelled or failed.</summary>
public sealed class VoiceHandshakeException : NyraException
{
    public VoiceHandshakeException(string message, Exception? innerException = null)
        : base(message, innerException ?? new TimeoutException(message)) { }
}
