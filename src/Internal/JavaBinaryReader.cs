using System.Buffers.Binary;
using System.Text;

namespace Nyra.Internal;

internal ref struct JavaBinaryReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _position;

    public JavaBinaryReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
    }

    public readonly int Remaining => _buffer.Length - _position;

    public byte ReadByte() => ReadBytes(1)[0];

    public bool ReadBoolean() => ReadByte() != 0;

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(2));

    public int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(ReadBytes(4));

    public long ReadInt64() => BinaryPrimitives.ReadInt64BigEndian(ReadBytes(8));

    public string ReadModifiedUtf8() => DecodeModifiedUtf8(ReadBytes(ReadUInt16()));

    public string? ReadNullableModifiedUtf8() => ReadBoolean() ? ReadModifiedUtf8() : null;

    private static string DecodeModifiedUtf8(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IndexOfAnyExceptInRange((byte)0, (byte)0x7F) < 0)
            return Encoding.ASCII.GetString(bytes);

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
            {
                if (first is >= 0xC0 and <= 0xF7)
                    throw Truncated(i, bytes.Length);
                throw InvalidByte(first, i);
            }

            var second = bytes[++i];
            if ((first & 0b1110_0000) == 0b1100_0000 && (second & 0b1100_0000) == 0b1000_0000)
            {
                chars[length++] = (char)(((first & 0b0001_1111) << 6) | (second & 0b0011_1111));
                continue;
            }

            if ((second & 0b1100_0000) != 0b1000_0000)
            {
                if (first is >= 0b1100_0000 and <= 0b1111_0111)
                    throw InvalidByte(second, i);
                throw InvalidByte(first, i - 1);
            }

            if (i + 1 >= bytes.Length)
            {
                if (first is >= 0xE0 and <= 0xF7)
                    throw Truncated(i, bytes.Length);
                throw InvalidByte(first, i - 1);
            }

            var third = bytes[++i];
            if ((first & 0b1111_0000) == 0b1110_0000 && (third & 0b1100_0000) == 0b1000_0000)
            {
                chars[length++] = (char)(
                    ((first & 0b0000_1111) << 12)
                    | ((second & 0b0011_1111) << 6)
                    | (third & 0b0011_1111)
                );
                continue;
            }

            if ((first & 0b1111_1000) == 0b1111_0000 && (third & 0b1100_0000) == 0b1000_0000)
            {
                if (i + 1 >= bytes.Length)
                    throw Truncated(i, bytes.Length);

                var fourth = bytes[++i];
                if ((fourth & 0b1100_0000) != 0b1000_0000)
                    throw InvalidByte(fourth, i);

                var codePoint =
                    ((first & 0b0000_0111) << 18)
                    | ((second & 0b0011_1111) << 12)
                    | ((third & 0b0011_1111) << 6)
                    | (fourth & 0b0011_1111);
                if (codePoint is < 0x10000 or > 0x10FFFF)
                    throw new InvalidDataException(
                        $"Invalid code point U+{codePoint:X} in modified UTF-8 string."
                    );

                codePoint -= 0x10000;
                chars[length++] = (char)(0xD800 | (codePoint >> 10));
                chars[length++] = (char)(0xDC00 | (codePoint & 0x3FF));
                continue;
            }

            throw InvalidByte(first, i);
        }

        return new string(chars, 0, length);
    }

    private static InvalidDataException InvalidByte(byte value, int offset) =>
        new($"Invalid byte 0x{value:X2} in modified UTF-8 string at byte {offset}.");

    private static InvalidDataException Truncated(int offset, int length) =>
        new(
            $"Truncated multi-byte sequence in modified UTF-8 string at byte {offset} of {length}."
        );

    private ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (_position + count > _buffer.Length)
            throw new EndOfStreamException(
                $"Unexpected end of the binary data at byte {_position} of {_buffer.Length}, {count} bytes were required."
            );

        var bytes = _buffer.Slice(_position, count);
        _position += count;
        return bytes;
    }
}
