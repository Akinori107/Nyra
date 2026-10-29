using System.Buffers.Binary;
using System.Text;
using Nyra.Internal;

namespace Nyra.Tests;

public sealed class JavaReader
{
    private static byte[] Wire(string text)
    {
        var body = Encoding.Latin1.GetBytes(text);
        var wire = new byte[2 + body.Length];
        BinaryPrimitives.WriteUInt16BigEndian(wire, (ushort)body.Length);
        body.CopyTo(wire, 2);
        return wire;
    }

    private static byte[] Wire(params byte[] body)
    {
        var wire = new byte[2 + body.Length];
        BinaryPrimitives.WriteUInt16BigEndian(wire, (ushort)body.Length);
        body.CopyTo(wire, 2);
        return wire;
    }

    private static string Decode(byte[] wire)
    {
        var reader = new JavaBinaryReader(wire);
        return reader.ReadModifiedUtf8();
    }

    [Test]
    public async Task Primitives()
    {
        var buffer = new byte[]
        {
            0x2A,
            0x01,
            0x12,
            0x34,
            0x12,
            0x34,
            0x56,
            0x78,
            0x12,
            0x34,
            0x56,
            0x78,
            0x9A,
            0xBC,
            0xDE,
            0xF0,
        };
        var reader = new JavaBinaryReader(buffer);
        var checked1 = reader.ReadByte();
        var checked2 = reader.ReadBoolean();
        var checked3 = reader.ReadUInt16();
        var checked4 = reader.ReadInt32();
        var checked5 = reader.ReadInt64();
        var checked6 = reader.Remaining;

        var flag = new JavaBinaryReader([0x00, 0xFF]);
        var checked7 = flag.ReadBoolean();
        var checked8 = flag.ReadBoolean();

        var minus = new JavaBinaryReader([(byte)0xFF, (byte)0xFF]);
        var checked9 = (short)minus.ReadUInt16();
        await Assert.That(checked1).IsEqualTo(0x2A);
        await Assert.That(checked2).IsTrue();
        await Assert.That(checked3).IsEqualTo(0x1234);
        await Assert.That(checked4).IsEqualTo(0x12345678);
        await Assert.That(checked5).IsEqualTo(0x123456789ABCDEF0);
        await Assert.That(checked6).IsEqualTo(0);
        await Assert.That(checked7).IsFalse();
        await Assert.That(checked8).IsTrue();
        await Assert.That(checked9).IsEqualTo(-1);
    }

    [Test]
    public async Task Bounds()
    {
        await Assert
            .That(() => new JavaBinaryReader([0x01]).ReadUInt16())
            .Throws<EndOfStreamException>();

        await Assert.That(() => new JavaBinaryReader([]).ReadByte()).Throws<EndOfStreamException>();

        await Assert.That(() => Decode(Wire(0x41, 0x42))).ThrowsNothing();
        await Assert
            .That(() => Decode(new byte[] { 0x00, 0x05, 0x41 }))
            .Throws<EndOfStreamException>();

        await Assert
            .That(() => new JavaBinaryReader([0x12, 0x34, 0x56]).ReadInt32())
            .Throws<EndOfStreamException>();
    }

    [Test]
    public async Task ModifiedUtf8Shapes()
    {
        await Assert.That(Decode(Wire("plain ascii"))).IsEqualTo("plain ascii");

        await Assert.That(Decode(Wire(0xC3, 0xA9))).IsEqualTo("\u00E9");

        await Assert.That(Decode(Wire(0xE2, 0x98, 0x83))).IsEqualTo("\u2603");

        await Assert.That(Decode(Wire(0xC0, 0x80))).IsEqualTo("\0");

        await Assert.That(Decode(Wire(0xED, 0xA0, 0xBD, 0xED, 0xB8, 0x80))).IsEqualTo("\U0001F600");

        await Assert.That(Decode(Wire(0xF0, 0x9F, 0x98, 0x80))).IsEqualTo("\U0001F600");

        var mixed = "a\u00E9\u2603\U0001F600z";
        await Assert
            .That(
                Decode(
                    Wire(
                        0x61,
                        0xC3,
                        0xA9,
                        0xE2,
                        0x98,
                        0x83,
                        0xED,
                        0xA0,
                        0xBD,
                        0xED,
                        0xB8,
                        0x80,
                        0x7A
                    )
                )
            )
            .IsEqualTo(mixed);
    }

    [Test]
    public async Task SurrogateDiscipline()
    {
        await Assert.That(Decode(Wire(0xED, 0xA0, 0xBD))).IsEqualTo("\uD83D");

        await Assert.That(Decode(Wire(0xED, 0xA0, 0xBD, 0x61))).IsEqualTo("\uD83Da");

        await Assert.That(Decode(Wire(0xED, 0xB8, 0x80))).IsEqualTo("\uDE00");
    }

    [Test]
    public async Task CorruptSequences()
    {
        await Assert
            .That(() =>
            {
                Decode(Wire(0xC3));
            })
            .Throws<InvalidDataException>();

        await Assert
            .That(() =>
            {
                Decode(Wire(0xF8));
            })
            .Throws<InvalidDataException>();

        await Assert
            .That(() =>
            {
                Decode(Wire(0xC3, 0x41));
            })
            .Throws<InvalidDataException>();

        await Assert
            .That(() =>
            {
                Decode(Wire(0x85, 0x61));
            })
            .Throws<InvalidDataException>();

        await Assert
            .That(() =>
            {
                Decode(Wire(0xE2, 0x98));
            })
            .Throws<InvalidDataException>();

        await Assert
            .That(() =>
            {
                Decode(Wire(0x80, 0x80));
            })
            .Throws<InvalidDataException>();

        await Assert
            .That(() =>
            {
                Decode(Wire(0xF0, 0x9F, 0x98));
            })
            .Throws<InvalidDataException>();

        await Assert
            .That(() =>
            {
                Decode(Wire(0xF0, 0x9F, 0x98, 0x41));
            })
            .Throws<InvalidDataException>();

        await Assert
            .That(() =>
            {
                Decode(Wire(0xF0, 0x80, 0x80, 0x80));
            })
            .Throws<InvalidDataException>();

        await Assert
            .That(() =>
            {
                Decode(Wire(0xF7, 0xBF, 0xBF, 0xBF));
            })
            .Throws<InvalidDataException>();

        await Assert
            .That(() =>
            {
                Decode(Wire(0xF8, 0x80, 0x80));
            })
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task NullableStrings()
    {
        var nullReader = new JavaBinaryReader([0x00]);
        var checked1 = nullReader.ReadNullableModifiedUtf8();

        var wire = Wire("present");
        var valueReader = new JavaBinaryReader([0x01, .. wire]);
        var checked2 = valueReader.ReadNullableModifiedUtf8();
        await Assert.That(checked1).IsNull();
        await Assert.That(checked2).IsEqualTo("present");
    }
}
