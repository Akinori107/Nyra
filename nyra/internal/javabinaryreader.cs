using System.Buffers.Binary;

namespace Nyra.Internal;

/// <summary>
/// Big-endian reader for the Java data format of encoded tracks: big-endian primitives,
/// <c>boolean</c> as one byte and strings in Java's modified UTF-8 (<c>DataOutputStream.writeUTF</c>).
/// </summary>
internal ref struct JavaBinaryReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public JavaBinaryReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _position = 0;
    }

    public readonly int Remaining => _buffer.Length - _position;

    public byte ReadByte()
    {
        EnsureAvailable(1);
        return _buffer[_position++];
    }

    public bool ReadBoolean() => ReadByte() != 0;

    public ushort ReadUInt16()
    {
        EnsureAvailable(2);
        var value = BinaryPrimitives.ReadUInt16BigEndian(_buffer.Slice(_position, 2));
        _position += 2;
        return value;
    }

    public int ReadInt32()
    {
        EnsureAvailable(4);
        var value = BinaryPrimitives.ReadInt32BigEndian(_buffer.Slice(_position, 4));
        _position += 4;
        return value;
    }

    public long ReadInt64()
    {
        EnsureAvailable(8);
        var value = BinaryPrimitives.ReadInt64BigEndian(_buffer.Slice(_position, 8));
        _position += 8;
        return value;
    }

    // Java's modified UTF-8: uint16 BE byte count, then that many bytes. Supplementary characters
    // arrive as two 3-byte surrogate sequences; standard 4-byte UTF-8 is tolerated too, for
    // robustness against non-conforming encoders.
    public string ReadModifiedUtf8()
    {
        var byteCount = ReadUInt16();
        EnsureAvailable(byteCount);
        var bytes = _buffer.Slice(_position, byteCount);
        _position += byteCount;
        return DecodeModifiedUtf8(bytes);
    }

    public string? ReadNullableModifiedUtf8() => ReadBoolean() ? ReadModifiedUtf8() : null;

    private static string DecodeModifiedUtf8(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        var length = 0;

        for (var i = 0; i < bytes.Length; i++)
        {
            var first = bytes[i];
            if ((first & 0b1000_0000) == 0)
            {
                chars[length++] = (char)first;
                continue;
            }

            if (i + 1 >= bytes.Length)
                throw new InvalidDataException(
                    "Truncated multi-byte sequence in modified UTF-8 string."
                );

            var second = bytes[++i];
            if ((first & 0b1110_0000) == 0b1100_0000 && (second & 0b1100_0000) == 0b1000_0000)
            {
                chars[length++] = (char)(((first & 0b0001_1111) << 6) | (second & 0b0011_1111));
                continue;
            }

            if (i + 1 >= bytes.Length)
                throw new InvalidDataException(
                    "Truncated multi-byte sequence in modified UTF-8 string."
                );

            var third = bytes[++i];
            if (
                (first & 0b1111_0000) == 0b1110_0000
                && (second & 0b1100_0000) == 0b1000_0000
                && (third & 0b1100_0000) == 0b1000_0000
            )
            {
                chars[length++] = (char)(
                    ((first & 0b0000_1111) << 12)
                    | ((second & 0b0011_1111) << 6)
                    | (third & 0b0011_1111)
                );
                continue;
            }

            // Not valid modified UTF-8. Standard UTF-8 4-byte sequences are tolerated by
            // decoding them into a surrogate pair, matching what non-Java encoders emit.
            if (
                (first & 0b1111_1000) == 0b1111_0000
                && (second & 0b1100_0000) == 0b1000_0000
                && (third & 0b1100_0000) == 0b1000_0000
            )
            {
                if (i + 2 >= bytes.Length)
                    throw new InvalidDataException(
                        "Truncated multi-byte sequence in modified UTF-8 string."
                    );

                var fourth = bytes[++i];
                if ((fourth & 0b1100_0000) != 0b1000_0000)
                    throw new InvalidDataException(
                        "Invalid byte sequence in modified UTF-8 string."
                    );

                var codePoint =
                    ((first & 0b0000_0111) << 18)
                    | ((second & 0b0011_1111) << 12)
                    | ((third & 0b0011_1111) << 6)
                    | (fourth & 0b0011_1111);
                if (codePoint is < 0x10000 or > 0x10FFFF)
                    throw new InvalidDataException("Invalid code point in modified UTF-8 string.");

                codePoint -= 0x10000;
                chars[length++] = (char)(0xD800 | (codePoint >> 10));
                chars[length++] = (char)(0xDC00 | (codePoint & 0x3FF));
                continue;
            }

            throw new InvalidDataException("Invalid byte sequence in modified UTF-8 string.");
        }

        return new string(chars, 0, length);
    }

    private readonly void EnsureAvailable(int count)
    {
        if (_position + count > _buffer.Length)
            throw new EndOfStreamException("The encoded track ended unexpectedly.");
    }
}
