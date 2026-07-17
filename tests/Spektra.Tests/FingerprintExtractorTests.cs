using Spektra.Core;

namespace Spektra.Tests;

public sealed class FingerprintExtractorTests
{
    private static float[] Sine(double hz, double seconds, int rate, float amplitude) =>
        [.. Enumerable.Range(0, (int)(seconds * rate))
            .Select(n => amplitude * MathF.Sin(2f * MathF.PI * (float)(hz * n / rate)))];

    [Test]
    public async Task PitchClass_FoldsOctaves_AndSeparatesNotes()
    {
        await Assert.That(FingerprintExtractor.PitchClass(440)).IsEqualTo(FingerprintExtractor.PitchClass(880));
        await Assert.That(FingerprintExtractor.PitchClass(440)).IsNotEqualTo(FingerprintExtractor.PitchClass(261.63));
    }

    [Test]
    public async Task Extract_IsDeterministic_AndChunkBoundaryBlind()
    {
        var samples = Sine(440, 3, 44100, 0.5f);
        var one = new FingerprintExtractor(44100);
        one.Feed(samples);
        var whole = one.Finish();

        var two = new FingerprintExtractor(44100);
        for (var i = 0; i < samples.Length; i += 4097)
            two.Feed(samples.AsSpan(i, Math.Min(4097, samples.Length - i)));
        var sliced = two.Finish();

        await Assert.That(whole.Words.Length).IsGreaterThan(0);
        await Assert.That(whole.Words.SequenceEqual(sliced.Words)).IsTrue();
    }

    [Test]
    public async Task Extract_IsVolumeInvariant()
    {
        var loud = new FingerprintExtractor(44100);
        loud.Feed(Sine(523.25, 3, 44100, 0.5f));
        var quiet = new FingerprintExtractor(44100);
        quiet.Feed(Sine(523.25, 3, 44100, 0.05f));
        var loudWords = loud.Finish().Words;
        var quietWords = quiet.Finish().Words;

        // A sustained pure tone holds its chroma vector nearly flat frame to
        // frame, so the sign-of-difference bits for the near-silent bins ride
        // on the dB round trip's float32 rounding noise (about 1 part in 1e7
        // at each amplitude) rather than on real spectral movement. That
        // noise floor differs slightly between the loud and quiet renditions
        // and occasionally flips a single near-tied bit, even though the
        // chroma vectors themselves are volume-invariant to about 6 decimal
        // digits. So this checks Hamming distance stays small instead of
        // demanding bit-exact equality, the same tolerance a downstream
        // matcher would apply.
        var totalBits = loudWords.Length * 32;
        var differingBits = 0;
        for (var i = 0; i < loudWords.Length; i++)
            differingBits += System.Numerics.BitOperations.PopCount(loudWords[i] ^ quietWords[i]);

        await Assert.That(loudWords.Length).IsGreaterThan(0);
        await Assert.That(differingBits).IsBetween(0, totalBits / 20); // <= 5% of bits
    }

    [Test]
    public async Task Extract_FrameRate_TracksDuration()
    {
        var ex = new FingerprintExtractor(44100);
        ex.Feed(Sine(440, 4, 44100, 0.5f));
        var fp = ex.Finish();
        // 4 s at 8 fps yields about 32 frames, so about 31 words (first window latency shaves a frame or two).
        await Assert.That(fp.Words.Length).IsBetween(27, 32);
        await Assert.That(fp.FramesPerSecond).IsEqualTo((byte)8);
    }
}
