namespace Nyra.Exceptions;

/// <summary>A malformed or unsupported encoded track.</summary>
/// <remarks>
/// Throw sites attach what they evaluated, <see cref="ByteOffset"/> and <see cref="PayloadLength"/> are
/// structured where the message alone would lose them. Offsets and lengths count bytes of the decoded
/// payload, zero-based, the four-byte header at offset 0, inner reader diagnostics count bytes of the
/// delimited message (after any legacy trailer trim) or of the individual string.
/// </remarks>
public sealed class InvalidTrackException : NyraException
{
    /// <summary>The byte offset the failure was evaluated at, null when the site had none.</summary>
    public int? ByteOffset { get; }

    /// <summary>The length of the payload being read, null when the site had none.</summary>
    public int? PayloadLength { get; }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one, null when there is none.</param>
    /// <param name="byteOffset">The byte offset the failure was evaluated at, null when the site had none.</param>
    /// <param name="payloadLength">The length of the payload being read, null when the site had none.</param>
    public InvalidTrackException(
        string message,
        Exception? innerException = null,
        int? byteOffset = null,
        int? payloadLength = null
    )
        : base(message, innerException)
    {
        ByteOffset = byteOffset;
        PayloadLength = payloadLength;
    }
}
