using Spektra.Core;

namespace Spektra.Tests;

public sealed class FingerprintTests
{
    [Test]
    public async Task Blob_RoundTrips()
    {
        var fp = new Fingerprint(8, [0u, 0xFFFFFFFFu, 0xDEADBEEFu]);
        var back = Fingerprint.FromBlob(fp.ToBlob());
        await Assert.That(back).IsNotNull();
        await Assert.That(back!.FramesPerSecond).IsEqualTo((byte)8);
        await Assert.That(back.Words.SequenceEqual(fp.Words)).IsTrue();
    }

    [Test]
    public async Task Blob_UnknownVersionOrTruncated_IsNull()
    {
        var blob = new Fingerprint(8, [1u, 2u]).ToBlob();
        blob[0] = 99;
        await Assert.That(Fingerprint.FromBlob(blob)).IsNull();
        await Assert.That(Fingerprint.FromBlob(new Fingerprint(8, [1u, 2u]).ToBlob()[..7])).IsNull();
    }

    [Test]
    public async Task DurationSeconds_CountsFrames()
    {
        // Words pair adjacent frames, so N words = N + 1 frames.
        await Assert.That(new Fingerprint(8, new uint[15]).DurationSeconds).IsEqualTo(2.0);
    }
}
