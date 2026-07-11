using Spektra.Core;

namespace Spektra.Tests;

public class SilenceScannerTests
{
    private const int Rate = 44100;

    private static float[] Tone(int count, float amp = 0.5f)
    {
        var s = new float[count];
        for (var i = 0; i < count; i++) s[i] = amp * MathF.Sin(2f * MathF.PI * 1000f * i / Rate);
        return s;
    }

    private static float[] Concat(params float[][] parts)
    {
        var n = parts.Sum(p => p.Length);
        var outp = new float[n];
        var o = 0;
        foreach (var p in parts) { Array.Copy(p, 0, outp, o, p.Length); o += p.Length; }
        return outp;
    }

    [Test]
    public async Task ContinuousAudio_HasNoDropouts()
    {
        var drops = SilenceScanner.FindDropouts([Tone(Rate * 3)], Rate);
        await Assert.That(drops).IsEmpty();
    }

    [Test]
    public async Task InteriorZeroGap_IsDetectedAtRightPlace()
    {
        // 1s tone, 1s of exact zero, 1s tone -> one gap from 1.0s to 2.0s.
        var signal = Concat(Tone(Rate), new float[Rate], Tone(Rate));
        var drops = SilenceScanner.FindDropouts([signal], Rate);
        await Assert.That(drops).HasSingleItem();
        await Assert.That(drops[0].StartSeconds).IsCloseTo(1.0, 0.01);
        await Assert.That(drops[0].EndSeconds).IsCloseTo(2.0, 0.01);
        await Assert.That(drops[0].DurationSeconds).IsCloseTo(1.0, 0.01);
    }

    [Test]
    public async Task LeadingAndTrailingSilence_IsIgnored()
    {
        var signal = Concat(new float[Rate], Tone(Rate), new float[Rate]);
        await Assert.That(SilenceScanner.FindDropouts([signal], Rate)).IsEmpty();
    }

    [Test]
    public async Task ShortGap_BelowThreshold_IsIgnored()
    {
        // 0.2s gap with a 0.5s threshold.
        var signal = Concat(Tone(Rate), new float[Rate / 5], Tone(Rate));
        await Assert.That(SilenceScanner.FindDropouts([signal], Rate)).IsEmpty();
    }

    [Test]
    public async Task GapDetection_WorksAcrossChunkBoundaries()
    {
        // Same gap, but split into many small chunks so the run spans boundaries.
        var signal = Concat(Tone(Rate), new float[Rate], Tone(Rate));
        var chunks = new List<float[]>();
        for (var i = 0; i < signal.Length; i += 999)
            chunks.Add(signal[i..Math.Min(i + 999, signal.Length)]);
        var drops = SilenceScanner.FindDropouts(chunks, Rate);
        await Assert.That(drops).HasSingleItem();
        await Assert.That(drops[0].DurationSeconds).IsCloseTo(1.0, 0.01);
    }
}
