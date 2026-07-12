using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Spektra.Core;

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

    // The pre-optimization MathNet cross-correlation, kept here as the oracle
    // the pooled implementation must match.
    private static (int Lag, double Confidence) Reference(float[] a, float[] b, int maxLag)
    {
        var need = Math.Max(a.Length, b.Length) + maxLag + 1;
        var n = 1;
        while (n < need * 2) n <<= 1;
        var fa = new Complex32[n];
        var fb = new Complex32[n];
        double ea = 0, eb = 0;
        for (var i = 0; i < a.Length; i++) { fa[i] = new Complex32(a[i], 0f); ea += (double)a[i] * a[i]; }
        for (var i = 0; i < b.Length; i++) { fb[i] = new Complex32(b[i], 0f); eb += (double)b[i] * b[i]; }
        Fourier.Forward(fa, FourierOptions.Matlab);
        Fourier.Forward(fb, FourierOptions.Matlab);
        var prod = new Complex32[n];
        for (var i = 0; i < n; i++) prod[i] = fa[i].Conjugate() * fb[i];
        Fourier.Inverse(prod, FourierOptions.Matlab);
        var bestLag = 0;
        var bestVal = float.NegativeInfinity;
        for (var l = -maxLag; l <= maxLag; l++)
        {
            var idx = ((l % n) + n) % n;
            var v = prod[idx].Real;
            if (v > bestVal) { bestVal = v; bestLag = l; }
        }
        var denom = Math.Sqrt(ea * eb);
        var conf = denom > 0 ? Math.Clamp(bestVal / denom, 0, 1) : 0;
        return (bestLag, conf);
    }

    [Test]
    public async Task EstimateLag_MatchesMathNetReference()
    {
        (int seed, int delay, int len)[] cases = [(11, 90, 4000), (12, 0, 2000), (13, 250, 6000)];
        foreach (var (seed, delay, len) in cases)
        {
            var a = Noise(len, seed);
            var b = new float[len];
            for (var i = delay; i < len; i++) b[i] = a[i - delay];

            var (lag, conf) = Aligner.EstimateLag(a, b, 500);
            var (refLag, refConf) = Reference(a, b, 500);

            await Assert.That(lag).IsEqualTo(refLag);
            await Assert.That(Math.Abs(conf - refConf) < 1e-4).IsTrue();
        }
    }

    [Test]
    public async Task EstimateLag_RecoversKnownDelay()
    {
        var a = Noise(4000, 1);
        const int delay = 137;
        var b = new float[a.Length];
        for (var i = delay; i < b.Length; i++) b[i] = a[i - delay]; // b = a delayed by 137
        var (lag, conf) = Aligner.EstimateLag(a, b, 500);
        await Assert.That(lag).IsEqualTo(delay);
        await Assert.That(conf > 0.5).IsTrue();
    }

    [Test]
    public async Task EstimateLag_ZeroDelay()
    {
        var a = Noise(2000, 2);
        var (lag, _) = Aligner.EstimateLag(a, (float[])a.Clone(), 200);
        await Assert.That(lag).IsEqualTo(0);
    }

    [Test]
    public async Task EstimateLag_Uncorrelated_LowConfidence()
    {
        var (_, conf) = Aligner.EstimateLag(Noise(2000, 3), Noise(2000, 4), 200);
        await Assert.That(conf < 0.3).IsTrue();
    }

    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Test]
    public async Task EstimateOffset_RecoversFixtureDelay()
    {
        var ff = FfmpegLocator.Locate([])!;
        var r = Aligner.EstimateOffset(ff,
            Path.Combine(Fixtures, "chirp.wav"),
            Path.Combine(Fixtures, "chirp-delay50ms.wav"),
            null, null);
        await Assert.That(r.Offset.TotalMilliseconds).IsBetween(45, 55); // B is A delayed by 50 ms
        await Assert.That(r.Confidence > 0.4).IsTrue();
    }
}
