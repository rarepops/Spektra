using System.Buffers.Binary;

namespace Spektra.Core;

/// A compact acoustic fingerprint: one 32-bit word per adjacent frame pair at
/// `FramesPerSecond`. Codec-, volume-, and offset-tolerant by construction
/// (see FingerprintExtractor). Serialized into the audit cache as a blob.
public sealed record Fingerprint(byte FramesPerSecond, uint[] Words)
{
    public const byte BlobVersion = 1;

    /// N words pair N + 1 frames; duration is the frame span.
    public double DurationSeconds => (double)(Words.Length + 1) / FramesPerSecond;

    public byte[] ToBlob()
    {
        var blob = new byte[2 + 4 + Words.Length * 4];
        blob[0] = BlobVersion;
        blob[1] = FramesPerSecond;
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(2), Words.Length);
        for (var i = 0; i < Words.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(6 + i * 4), Words[i]);
        return blob;
    }

    /// Null on unknown version or a short/inconsistent blob: the caller treats
    /// it as a cache miss and re-extracts.
    public static Fingerprint? FromBlob(byte[] blob)
    {
        if (blob.Length < 6 || blob[0] != BlobVersion) return null;
        var count = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(2));
        if (count < 0 || blob.Length != 6 + count * 4) return null;
        var words = new uint[count];
        for (var i = 0; i < count; i++)
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(blob.AsSpan(6 + i * 4));
        return new Fingerprint(blob[1], words);
    }
}
