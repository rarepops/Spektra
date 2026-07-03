using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class AlignerTests
{
    private static float[] Noise(int n, int seed)
    {
        var rng = new Random(seed);
        var a = new float[n];
        for (var i = 0; i < n; i++) a[i] = (float)(rng.NextDouble() * 2 - 1);
        return a;
    }

    [Fact]
    public void EstimateLag_RecoversKnownDelay()
    {
        var a = Noise(4000, 1);
        const int delay = 137;
        var b = new float[a.Length];
        for (var i = delay; i < b.Length; i++) b[i] = a[i - delay]; // b = a delayed by 137
        var (lag, conf) = Aligner.EstimateLag(a, b, 500);
        Assert.Equal(delay, lag);
        Assert.True(conf > 0.5, $"confidence {conf}");
    }

    [Fact]
    public void EstimateLag_ZeroDelay()
    {
        var a = Noise(2000, 2);
        var (lag, _) = Aligner.EstimateLag(a, (float[])a.Clone(), 200);
        Assert.Equal(0, lag);
    }

    [Fact]
    public void EstimateLag_Uncorrelated_LowConfidence()
    {
        var (_, conf) = Aligner.EstimateLag(Noise(2000, 3), Noise(2000, 4), 200);
        Assert.True(conf < 0.3, $"confidence {conf}");
    }

    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public void EstimateOffset_RecoversFixtureDelay()
    {
        var ff = FfmpegLocator.Locate([])!;
        var r = Aligner.EstimateOffset(ff,
            Path.Combine(Fixtures, "chirp.wav"),
            Path.Combine(Fixtures, "chirp-delay50ms.wav"),
            null, null);
        Assert.InRange(r.Offset.TotalMilliseconds, 45, 55); // B is A delayed by 50 ms
        Assert.True(r.Confidence > 0.4, $"confidence {r.Confidence}");
    }
}
