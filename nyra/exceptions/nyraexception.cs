namespace Nyra.Exceptions;

/// <summary>Base class of the errors raised by Nyra.</summary>
public class NyraException : Exception
{
    protected NyraException(string message)
        : base(message) { }

    protected NyraException(string message, Exception innerException)
        : base(message, innerException) { }
}
