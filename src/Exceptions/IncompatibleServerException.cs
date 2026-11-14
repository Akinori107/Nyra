namespace Nyra.Exceptions;

/// <summary>
/// The connected server does not expose /v4/info, did not report a version, is below the minimum supported
/// version, or answered with a body that is not JSON.
/// </summary>
/// <param name="message">The message that describes the error.</param>
/// <param name="innerException">The exception that caused this one, null when there is none.</param>
public sealed class IncompatibleServerException(string message, Exception? innerException = null)
    : NyraException(message, innerException) { }
