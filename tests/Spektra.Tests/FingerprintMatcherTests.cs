using Spektra.Core;

namespace Spektra.Tests;

public sealed class FingerprintMatcherTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;

    private static Fingerprint Fp(string file)
    {
        var path = Path.Combine(Fixtures, file);
        var meta = new AnalysisSession(Ff).ReadMetadata(path);
        return new DecodedFingerprintSource(Ff).Extract(path, meta, CancellationToken.None);
    }

    [Test]
    [Arguments("chirp-aac.m4a")]
    public async Task SameRecordingAcrossCodecs_ClearsTheHighBar(string other)
    {
        var m = FingerprintMatcher.Match(Fp("chirp.wav"), Fp(other));
        await Assert.That(m).IsNotNull();
        await Assert.That(m!.Similarity).IsGreaterThanOrEqualTo(FingerprintMatcher.HighThreshold);
        await Assert.That(Math.Abs(m.OffsetFrames)).IsLessThanOrEqualTo(2);
    }

    [Test]
    public async Task SameRecordingSurvivesLossy_OnInBandContent()
    {
        // The survives-lossy guarantee, pinned on content the fingerprint can
        // actually see: chords and a melody inside the 55-3520 Hz chroma band.
        var m = FingerprintMatcher.Match(Fp("tones-a.wav"), Fp("tones-a-128.mp3"));
        await Assert.That(m).IsNotNull();
        await Assert.That(m!.Similarity).IsGreaterThanOrEqualTo(FingerprintMatcher.HighThreshold);
        await Assert.That(Math.Abs(m.OffsetFrames)).IsLessThanOrEqualTo(2);
    }

    [Test]
    public async Task SameKeyDifferentMelody_NeverPairs()
    {
        // The owner-library false positive of 2026-08-05 as audio: same bass
        // drone, same key, different melody. Static-profile agreement must
        // never read as "same recording".
        var m = FingerprintMatcher.Match(Fp("tones-a.wav"), Fp("tones-b.wav"));
        await Assert.That(m is null || m.Similarity < FingerprintMatcher.MidThreshold).IsTrue();
    }

    [Test]
    public async Task HeavilyDamagedSweep_StillAligns_ButIsNoLongerAStrongPositive()
    {
        // chirp-mp3-64 used to be pinned at the High bar, and that pass was an
        // artifact: the sweep leaves the 55-3520 Hz chroma band at ~0.5 s, so
        // five-sixths of the fixture is spectrally empty and its old score rode
        // on the sparse-word floor that chance correction now subtracts. What
        // is honestly left of the pair is its alignment, so that is what stays
        // pinned.
        var m = FingerprintMatcher.Match(Fp("chirp.wav"), Fp("chirp-mp3-64.mp3"));
        await Assert.That(m).IsNotNull();
        await Assert.That(Math.Abs(m!.OffsetFrames)).IsLessThanOrEqualTo(2);
    }

    [Test]
    public async Task PaddedCopy_AlignsAtTwoSeconds()
    {
        var m = FingerprintMatcher.Match(Fp("chirp.wav"), Fp("chirp-padded2s.wav"));
        await Assert.That(m).IsNotNull();
        await Assert.That(m!.Similarity).IsGreaterThanOrEqualTo(FingerprintMatcher.HighThreshold);
        await Assert.That(m.OffsetFrames).IsBetween(14, 18); // 2 s at 8 fps, plus window latency slack
    }

    [Test]
    [Arguments("noise.wav")]
    [Arguments("sine-1khz.wav")]
    [Arguments("chirp-pitchup.wav")]
    public async Task DifferentAudio_StaysBelowTheMidBar(string other)
    {
        var m = FingerprintMatcher.Match(Fp("chirp.wav"), Fp(other));
        await Assert.That(m is null || m.Similarity < FingerprintMatcher.MidThreshold).IsTrue();
    }

    [Test]
    public async Task AlignedButDifferentAudio_IsScored_BelowTheMidBar()
    {
        // The fixture negatives above never reach scoring (too few votes), so
        // this is the only cover for the BER path on a pair that aligns
        // without being the same recording: 10 shared anchor words carry the
        // offset-0 vote past MinVotes, while the rest of the overlap (30
        // words per side, all-1s vs. all-0s bit patterns) disagrees on every
        // bit.
        uint[] anchors = [.. Enumerable.Range(1, 10).Select(n => (uint)n)];
        var a = new Fingerprint(8, [.. anchors, .. Enumerable.Repeat(0x55555555u, 30)]);
        var b = new Fingerprint(8, [.. anchors, .. Enumerable.Repeat(0xAAAAAAAAu, 30)]);

        var m = FingerprintMatcher.Match(a, b);
        await Assert.That(m).IsNotNull();
        await Assert.That(m!.OffsetFrames).IsEqualTo(0);
        await Assert.That(m.Similarity).IsLessThan(FingerprintMatcher.MidThreshold);
    }

    // Deterministic pseudo-noise; tests must not use Random.
    private static uint Lcg(ref uint state) => state = state * 1664525u + 1013904223u;

    /// A track whose words share one static bit profile (a key/chord colour)
    /// plus per-frame noise, with an exact anchor word every 8th frame so the
    /// pair aligns. This is the shape of two DIFFERENT songs in the same key:
    /// the 12 spectral bits of every word encode the chroma profile, so two
    /// same-key tracks agree on most of them all track long.
    private static Fingerprint SameProfileTrack(uint seed, int words = 640)
    {
        const uint profile = 0x0FF0A5C3u;
        var state = seed;
        var w = new uint[words];
        for (var t = 0; t < words; t++)
        {
            if (t % 8 == 0) { w[t] = profile; continue; }
            var noise = 0u;
            for (var k = 0; k < 5; k++)
                noise |= 1u << (int)(Lcg(ref state) >> 27); // top bits: LCG low bits cycle seed-independently
            w[t] = profile ^ noise;
        }
        return new Fingerprint(8, w);
    }

    [Test]
    public async Task SameProfileStrangers_StayBelowTheMidBar()
    {
        // Regression for a real false positive (owner library, 2026-08-05):
        // two different electronic tracks in the same key grouped at 0.41.
        // Their static profile agreement lifts raw bit similarity into the
        // 0.40-0.45 band over a full-length overlap at offset 0, which is
        // indistinguishable from a weak true match unless the score is
        // measured AGAINST that pair's own wrong-alignment baseline.
        var a = SameProfileTrack(seed: 12345);
        var b = SameProfileTrack(seed: 99991);

        var m = FingerprintMatcher.Match(a, b);
        await Assert.That(m).IsNotNull();
        await Assert.That(m!.OverlapFraction).IsGreaterThanOrEqualTo(FingerprintMatcher.FullConfidenceOverlap);
        await Assert.That(m.Similarity).IsLessThan(FingerprintMatcher.MidThreshold);
    }

    [Test]
    public async Task TrueCopyWithTemporalStructure_KeepsTheHighBar()
    {
        // The counterpart guard: baseline normalization must not eat a real
        // match. A structured track's wrong-alignment baseline is near zero,
        // so its normalized similarity stays where the raw one was.
        var state = 777u;
        var words = new uint[640];
        for (var t = 0; t < words.Length; t++) words[t] = Lcg(ref state);
        var a = new Fingerprint(8, words);
        var damaged = (uint[])words.Clone();
        for (var t = 0; t < damaged.Length; t += 16) damaged[t] ^= 1u;
        var b = new Fingerprint(8, damaged);

        var m = FingerprintMatcher.Match(a, b);
        await Assert.That(m).IsNotNull();
        await Assert.That(m!.OffsetFrames).IsEqualTo(0);
        await Assert.That(m.Similarity).IsGreaterThanOrEqualTo(FingerprintMatcher.HighThreshold);
    }

    [Test]
    public async Task EmptyOrMismatchedRate_IsNull()
    {
        await Assert.That(FingerprintMatcher.Match(new Fingerprint(8, []), new Fingerprint(8, [1u]))).IsNull();
        await Assert.That(FingerprintMatcher.Match(new Fingerprint(8, [1u]), new Fingerprint(4, [1u]))).IsNull();
    }
}
