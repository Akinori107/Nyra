namespace Nyra.Exceptions;

/// <summary>A malformed or unsupported encoded track.</summary>
/// <remarks>
/// <see cref="ByteOffset"/> and <see cref="PayloadLength"/> count bytes of the decoded track, when known.
/// </remarks>
/// <param name="message">The message that describes the error.</param>
/// <param name="innerException">The exception that caused this one, null when there is none.</param>
/// <param name="byteOffset">The byte offset the failure was evaluated at, null when unknown.</param>
/// <param name="payloadLength">The length of the payload being read, null when unknown.</param>
public sealed class InvalidTrackException(
    string message,
    Exception? innerException = null,
    int? byteOffset = null,
    int? payloadLength = null
) : NyraException(message, innerException)
{
    /// <summary>The byte offset the failure was evaluated at, null when unknown.</summary>
    public int? ByteOffset { get; } = byteOffset;

    /// <summary>The length of the payload being read, null when unknown.</summary>
    public int? PayloadLength { get; } = payloadLength;
}
