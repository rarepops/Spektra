using Spektra.Core;

namespace Spektra.Tests;

public sealed class FingerprintIndexTests
{
    private static Fingerprint Fp(params uint[] words) => new(8, words);

    [Test]
    public async Task PairsFiles_SharingEnoughWords()
    {
        var index = new FingerprintIndex();
        uint[] shared = [.. Enumerable.Range(100, 10).Select(n => (uint)n)];
        var a = index.Add(Fp(shared));
        var b = index.Add(Fp([.. shared, 999u]));
        var c = index.Add(Fp([.. Enumerable.Range(5000, 10).Select(n => (uint)n)]));

        var pairs = index.CandidatePairs(minSharedWords: 8);
        await Assert.That(pairs).HasSingleItem();
        await Assert.That(pairs[0]).IsEqualTo((a, b));
        await Assert.That(c).IsEqualTo(2);
    }

    [Test]
    public async Task UbiquitousWords_AreIgnored()
    {
        // One word shared by more files than MaxPostingFanout is background
        // noise (near-silence frames), not evidence of a duplicate.
        var index = new FingerprintIndex();
        for (var i = 0; i < FingerprintIndex.MaxPostingFanout + 6; i++)
            index.Add(Fp(42u));
        await Assert.That(index.CandidatePairs(minSharedWords: 1)).IsEmpty();
    }
}
