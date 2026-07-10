using System.Buffers.Binary;
using System.IO.Compression;

namespace Spektra.Core;

/// Minimal PNG encoder: 8-bit RGB (color type 2), one zlib IDAT, filter 0
/// scanlines. Only what Spektra needs to write spectrogram bitmaps.
public static class PngWriter
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// rgb is row-major, top row first, 3 bytes per pixel;
    /// length must equal width * height * 3.
    public static void Write(Stream output, int width, int height, byte[] rgb)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (rgb.Length != (long)width * height * 3)
            throw new ArgumentException("rgb length must be width * height * 3.", nameof(rgb));

        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 2;   // color type: truecolor RGB
        ihdr[10] = 0;  // compression method
        ihdr[11] = 0;  // filter method
        ihdr[12] = 0;  // interlace
        WriteChunk(output, "IHDR"u8, ihdr);

        using (var idat = new MemoryStream())
        {
            using (var z = new ZLibStream(idat, CompressionLevel.Optimal, leaveOpen: true))
            {
                var stride = width * 3;
                for (var y = 0; y < height; y++)
                {
                    z.WriteByte(0); // filter: None
                    z.Write(rgb, y * stride, stride);
                }
            }
            WriteChunk(output, "IDAT"u8, idat.GetBuffer().AsSpan(0, (int)idat.Length));
        }

        WriteChunk(output, "IEND"u8, []);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        output.Write(len);
        output.Write(type);
        output.Write(data);

        var crc = Crc32.Begin();
        crc = Crc32.Update(crc, type);
        crc = Crc32.Update(crc, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, Crc32.End(crc));
        output.Write(crcBytes);
    }

    /// CRC-32 as PNG specifies it (ISO 3309, reflected polynomial 0xEDB88320),
    /// table-based. System.IO.Hashing would provide this but is a separate
    /// NuGet package; twenty lines keep the project dependency-free.
    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        public static uint Begin() => 0xFFFFFFFFu;

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        public static uint End(uint crc) => crc ^ 0xFFFFFFFFu;
    }
}
