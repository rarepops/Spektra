using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class DynamicsTests
{
    [Fact]
    public void Sine_CrestIsAboutThreeDb()
    {
        var s = new float[44100];
        for (var i = 0; i < s.Length; i++) s[i] = 0.5f * MathF.Sin(2f * MathF.PI * 1000f * i / 44100f);
        var d = Dynamics.FromSamples([s]);
        Assert.InRange(d.CrestFactorDb, 2.5f, 3.5f);   // sine crest factor ~3.01 dB
        Assert.InRange(d.PeakDbfs, -6.5f, -5.5f);       // 0.5 amplitude ~ -6 dBFS
        Assert.Equal(0, d.ClippedSamples);
    }

    [Fact]
    public void Clipping_IsCounted()
    {
        var d = Dynamics.FromSamples([[0.1f, 1.0f, -1.0f, 0.999f, 0.2f, 1.0f]]);
        Assert.Equal(3, d.ClippedSamples);  // 1.0, -1.0, 1.0 (0.999 is below the 0.9997 threshold)
        Assert.Equal(6, d.TotalSamples);
        Assert.Equal(0.5, d.ClippedFraction, 3);
    }

    [Fact]
    public void Empty_IsSafe()
    {
        var d = Dynamics.FromSamples([]);
        Assert.Equal(0, d.TotalSamples);
        Assert.Equal(0, d.ClippedFraction, 3);
    }
}

public class LoudnessMeasurerTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;

    private const string Summary = """
        [Parsed_ebur128_0 @ 000] t: 1.0  I:  -99.0 LUFS  LRA:  99.0 LU  TPK:  -99.0 dBFS
        [Parsed_ebur128_0 @ 000] Summary:
          Integrated loudness:
            I:          -3.9 LUFS
            Threshold: -13.9 LUFS
          Loudness range:
            LRA:        20.0 LU
            Threshold: -23.9 LUFS
            LRA low:   -23.9 LUFS
            LRA high:   -3.9 LUFS
          True peak:
            Peak:       -0.9 dBFS
        """;

    [Fact]
    public void ParseEbur128_ReadsSummary_IgnoringProgressLines()
    {
        var (i, lra, tp) = LoudnessMeasurer.ParseEbur128(Summary);
        Assert.Equal(-3.9, i!.Value, 3);
        Assert.Equal(20.0, lra!.Value, 3);
        Assert.Equal(-0.9, tp!.Value, 3);
    }

    [Fact]
    public void ParseEbur128_MissingSummary_ReturnsNulls()
    {
        var (i, lra, tp) = LoudnessMeasurer.ParseEbur128("no summary here");
        Assert.Null(i);
        Assert.Null(lra);
        Assert.Null(tp);
    }

    [Fact]
    public void Measure_Sine_ReturnsLoudnessAndCrest()
    {
        var r = new LoudnessMeasurer(Ff).Measure(Path.Combine(Fixtures, "sine-1khz.wav"));
        Assert.NotNull(r.IntegratedLufs);
        Assert.NotNull(r.TruePeakDbtp);
        Assert.InRange(r.TruePeakDbtp!.Value, -2.0, 0.0);
        Assert.InRange(r.CrestFactorDb, 2.5f, 3.5f);
    }
}
