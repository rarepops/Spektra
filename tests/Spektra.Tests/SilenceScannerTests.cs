using Spektra.Core;
using Xunit;

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

    [Fact]
    public void ContinuousAudio_HasNoDropouts()
    {
        var drops = SilenceScanner.FindDropouts([Tone(Rate * 3)], Rate);
        Assert.Empty(drops);
    }

    [Fact]
    public void InteriorZeroGap_IsDetectedAtRightPlace()
    {
        // 1s tone, 1s of exact zero, 1s tone -> one gap from 1.0s to 2.0s.
        var signal = Concat(Tone(Rate), new float[Rate], Tone(Rate));
        var drops = SilenceScanner.FindDropouts([signal], Rate);
        Assert.Single(drops);
        Assert.Equal(1.0, drops[0].StartSeconds, 2);
        Assert.Equal(2.0, drops[0].EndSeconds, 2);
        Assert.Equal(1.0, drops[0].DurationSeconds, 2);
    }

    [Fact]
    public void LeadingAndTrailingSilence_IsIgnored()
    {
        var signal = Concat(new float[Rate], Tone(Rate), new float[Rate]);
        Assert.Empty(SilenceScanner.FindDropouts([signal], Rate));
    }

    [Fact]
    public void ShortGap_BelowThreshold_IsIgnored()
    {
        // 0.2s gap with a 0.5s threshold.
        var signal = Concat(Tone(Rate), new float[Rate / 5], Tone(Rate));
        Assert.Empty(SilenceScanner.FindDropouts([signal], Rate));
    }

    [Fact]
    public void GapDetection_WorksAcrossChunkBoundaries()
    {
        // Same gap, but split into many small chunks so the run spans boundaries.
        var signal = Concat(Tone(Rate), new float[Rate], Tone(Rate));
        var chunks = new List<float[]>();
        for (var i = 0; i < signal.Length; i += 999)
            chunks.Add(signal[i..Math.Min(i + 999, signal.Length)]);
        var drops = SilenceScanner.FindDropouts(chunks, Rate);
        Assert.Single(drops);
        Assert.Equal(1.0, drops[0].DurationSeconds, 2);
    }
}
