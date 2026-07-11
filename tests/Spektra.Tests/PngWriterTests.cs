using System.Buffers.Binary;
using System.IO.Compression;
using Spektra.Core;

namespace Spektra.Tests;

public sealed class PngWriterTests
{
    private static readonly byte[] Rgb3x2 =
    [
        255, 0, 0,   0, 255, 0,   0, 0, 255,   // top row: red, green, blue
        10, 20, 30,  40, 50, 60,  70, 80, 90,  // bottom row
    ];

    private static byte[] Encode(int w, int h, byte[] rgb)
    {
        using var ms = new MemoryStream();
        PngWriter.Write(ms, w, h, rgb);
        return ms.ToArray();
    }

    [Test]
    public async Task Write_EmitsSignatureAndIhdr()
    {
        var png = Encode(3, 2, Rgb3x2);
        await Assert.That(png[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })).IsTrue();
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(8))).IsEqualTo(13);  // IHDR data length
        await Assert.That(png[12..16].SequenceEqual("IHDR"u8.ToArray())).IsTrue();
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16))).IsEqualTo(3); // width
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))).IsEqualTo(2); // height
        await Assert.That((int)png[24]).IsEqualTo(8);  // bit depth
        await Assert.That((int)png[25]).IsEqualTo(2);  // color type: truecolor RGB
        await Assert.That((int)png[26]).IsEqualTo(0);  // compression
        await Assert.That((int)png[27]).IsEqualTo(0);  // filter method
        await Assert.That((int)png[28]).IsEqualTo(0);  // interlace
    }

    [Test]
    public async Task Write_EndsWithCanonicalIendChunk()
    {
        var png = Encode(3, 2, Rgb3x2);
        // Every valid PNG ends with these exact 12 bytes; the CRC 0xAE426082
        // is a published constant, an independent check of our CRC-32.
        await Assert.That(png[^12..].SequenceEqual(
            new byte[] { 0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82 })).IsTrue();
    }

    [Test]
    public async Task Write_RoundTripsPixels()
    {
        var png = Encode(3, 2, Rgb3x2);
        var idat = FindChunk(png, "IDAT");
        using var z = new ZLibStream(new MemoryStream(idat), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var scan = raw.ToArray();

        const int stride = 3 * 3;
        await Assert.That(scan.Length).IsEqualTo(2 * (1 + stride));
        for (var y = 0; y < 2; y++)
        {
            await Assert.That((int)scan[y * (1 + stride)]).IsEqualTo(0); // per-scanline filter byte: None
            await Assert.That(scan[(y * (1 + stride) + 1)..((y + 1) * (1 + stride))]
                .AsEnumerable().SequenceEqual(Rgb3x2[(y * stride)..((y + 1) * stride)])).IsTrue();
        }
    }

    [Test]
    public async Task Write_ValidatesArguments()
    {
        using var ms = new MemoryStream();
        await Assert.That(() => PngWriter.Write(ms, 0, 2, Rgb3x2)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => PngWriter.Write(ms, 3, 0, Rgb3x2)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => PngWriter.Write(ms, 3, 3, Rgb3x2)).ThrowsExactly<ArgumentException>();
    }

    private static byte[] FindChunk(byte[] png, string type)
    {
        var pos = 8;
        while (pos < png.Length)
        {
            var len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            var t = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            if (t == type) return png[(pos + 8)..(pos + 8 + len)];
            pos += 12 + len; // length + type + data + crc
        }
        throw new InvalidOperationException($"chunk {type} not found");
    }
}
