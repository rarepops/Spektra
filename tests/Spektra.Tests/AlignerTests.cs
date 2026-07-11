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
