namespace Nyra.Exceptions;

/// <summary>Base class of the errors raised by Nyra, catching it groups them all at once.</summary>
/// <remarks>
/// The concrete exceptions are sealed, the open base is the extension point, derive from it to raise custom
/// Nyra-family errors.
/// </remarks>
public class NyraException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    protected NyraException(string message)
        : base(message) { }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one, null when there is none.</param>
    protected NyraException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
