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
    [Arguments("chirp-mp3-64.mp3")]
    [Arguments("chirp-aac.m4a")]
    public async Task SameRecordingAcrossCodecs_ClearsTheHighBar(string other)
    {
        var m = FingerprintMatcher.Match(Fp("chirp.wav"), Fp(other));
        await Assert.That(m).IsNotNull();
        await Assert.That(m!.Similarity).IsGreaterThanOrEqualTo(FingerprintMatcher.HighThreshold);
        await Assert.That(Math.Abs(m.OffsetFrames)).IsLessThanOrEqualTo(2);
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
    public async Task EmptyOrMismatchedRate_IsNull()
    {
        await Assert.That(FingerprintMatcher.Match(new Fingerprint(8, []), new Fingerprint(8, [1u]))).IsNull();
        await Assert.That(FingerprintMatcher.Match(new Fingerprint(8, [1u]), new Fingerprint(4, [1u]))).IsNull();
    }
}
