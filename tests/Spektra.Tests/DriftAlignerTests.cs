using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class DriftAlignerTests
{
    private static float[] Noise(int n, int seed)
    {
        var rng = new Random(seed);
        var a = new float[n];
        for (var i = 0; i < n; i++) a[i] = (float)(rng.NextDouble() * 2 - 1);
        return a;
    }

    private static float[] Delay(float[] a, int d)
    {
        var b = new float[a.Length];
        for (var i = d; i < b.Length; i++) b[i] = a[i - d];
        return b;
    }

    [Fact]
    public void ConstantDelay_YieldsConstantLag_NearZeroSpread()
    {
        var a = Noise(24000, 1);
        var b = Delay(a, 60);
        var anchors = DriftAligner.EstimateDrift(a, b, 4, 200);
        Assert.Equal(4, anchors.Count);
        Assert.All(anchors, an => Assert.InRange(an.Lag, 55, 65));
        Assert.True(DriftAligner.LagSpread(anchors) <= 8, "constant delay should not drift");
    }

    [Fact]
    public void PiecewiseDelay_ShowsDifferentLags_LargeSpread()
    {
        // First half delayed by 20, second half by 90.
        var a = Noise(24000, 2);
        var b = new float[a.Length];
        var mid = a.Length / 2;
        for (var i = 20; i < mid; i++) b[i] = a[i - 20];
        for (var i = mid + 90; i < b.Length; i++) b[i] = a[i - 90];

        var anchors = DriftAligner.EstimateDrift(a, b, 4, 200);
        Assert.True(DriftAligner.LagSpread(anchors) > 40,
            $"piecewise delay should produce a large lag spread, got {DriftAligner.LagSpread(anchors)}");
    }

    [Fact]
    public void TooShort_YieldsNoAnchors()
    {
        Assert.Empty(DriftAligner.EstimateDrift(new float[4], new float[4], 4, 2));
    }
}
