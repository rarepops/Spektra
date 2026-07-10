using System.Buffers.Binary;
using System.IO.Compression;
using Spektra.Core;
using Xunit;

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

    [Fact]
    public void Write_EmitsSignatureAndIhdr()
    {
        var png = Encode(3, 2, Rgb3x2);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);
        Assert.Equal(13, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(8)));  // IHDR data length
        Assert.Equal("IHDR"u8.ToArray(), png[12..16]);
        Assert.Equal(3, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16))); // width
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))); // height
        Assert.Equal(8, png[24]);  // bit depth
        Assert.Equal(2, png[25]);  // color type: truecolor RGB
        Assert.Equal(0, png[26]);  // compression
        Assert.Equal(0, png[27]);  // filter method
        Assert.Equal(0, png[28]);  // interlace
    }

    [Fact]
    public void Write_EndsWithCanonicalIendChunk()
    {
        var png = Encode(3, 2, Rgb3x2);
        // Every valid PNG ends with these exact 12 bytes; the CRC 0xAE426082
        // is a published constant, an independent check of our CRC-32.
        Assert.Equal(
            [0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82],
            png[^12..]);
    }

    [Fact]
    public void Write_RoundTripsPixels()
    {
        var png = Encode(3, 2, Rgb3x2);
        var idat = FindChunk(png, "IDAT");
        using var z = new ZLibStream(new MemoryStream(idat), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var scan = raw.ToArray();

        const int stride = 3 * 3;
        Assert.Equal(2 * (1 + stride), scan.Length);
        for (var y = 0; y < 2; y++)
        {
            Assert.Equal(0, scan[y * (1 + stride)]); // per-scanline filter byte: None
            Assert.Equal(Rgb3x2[(y * stride)..((y + 1) * stride)],
                         scan[(y * (1 + stride) + 1)..((y + 1) * (1 + stride))]);
        }
    }

    [Fact]
    public void Write_ValidatesArguments()
    {
        using var ms = new MemoryStream();
        Assert.Throws<ArgumentOutOfRangeException>(() => PngWriter.Write(ms, 0, 2, Rgb3x2));
        Assert.Throws<ArgumentOutOfRangeException>(() => PngWriter.Write(ms, 3, 0, Rgb3x2));
        Assert.Throws<ArgumentException>(() => PngWriter.Write(ms, 3, 3, Rgb3x2));
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
