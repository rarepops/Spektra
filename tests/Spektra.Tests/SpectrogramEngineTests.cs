using Spektra.Core;

namespace Spektra.Tests;

public class SpectrogramEngineTests
{
    private static IEnumerable<float[]> Chunks(float[] all, int chunkSize = 10_000)
    {
        for (var i = 0; i < all.Length; i += chunkSize)
            yield return all.AsSpan(i, Math.Min(chunkSize, all.Length - i)).ToArray();
    }

    private static float[] Sine(float freq, int sampleRate, int count, float amp = 1f)
    {
        var s = new float[count];
        for (var n = 0; n < count; n++)
            s[n] = amp * MathF.Sin(2f * MathF.PI * freq * n / sampleRate);
        return s;
    }

    [Test]
    public async Task Sine_ProducesPeakInCorrectBin_EveryColumn()
    {
        var engine = new SpectrogramEngine(new SpectrogramSettings());
        var samples = Sine(1000f, 44100, 44100 * 3);
        var columns = engine.Columns(Chunks(samples), samples.Length, CancellationToken.None).ToList();

        // (132300 - 2048) / 1024 + 1 = 128 windows
        await Assert.That(columns.Count).IsBetween(120, 130);
        await Assert.That(columns.All(c => c.Length == 1025)).IsTrue();
        foreach (var col in columns)
        {
            var peak = 0;
            for (var k = 1; k < col.Length; k++) if (col[k] > col[peak]) peak = k;
            await Assert.That(peak).IsBetween(46, 47);
            await Assert.That(col[peak] > -2f).IsTrue();
        }
    }

    [Test]
    public async Task Chirp_PeakBinRises()
    {
        var engine = new SpectrogramEngine(new SpectrogramSettings());
        const int sr = 44100, total = sr * 3;
        var samples = new float[total];
        for (var n = 0; n < total; n++)
        {
            var t = (float)n / sr;
            samples[n] = 0.8f * MathF.Sin(2f * MathF.PI * 3675f * t * t);
        }
        var columns = engine.Columns(Chunks(samples), total, CancellationToken.None).ToList();

        static int ArgMax(float[] c) { var p = 0; for (var k = 1; k < c.Length; k++) if (c[k] > c[p]) p = k; return p; }
        await Assert.That(ArgMax(columns[^10]) > ArgMax(columns[5]) + 400).IsTrue(); // chirp should rise
    }

    [Test]
    public async Task WhiteNoise_SpectrumIsApproximatelyFlat()
    {
        var engine = new SpectrogramEngine(new SpectrogramSettings());
        var rng = new Random(42);
        var samples = new float[44100 * 3];
        for (var n = 0; n < samples.Length; n++)
            samples[n] = (float)(rng.NextDouble() * 2 - 1) * 0.5f;
        var columns = engine.Columns(Chunks(samples), samples.Length, CancellationToken.None).ToList();

        // average each bin over all columns, then compare mid vs high band
        var mean = new double[1025];
        foreach (var col in columns)
            for (var k = 0; k < mean.Length; k++) mean[k] += col[k];
        for (var k = 0; k < mean.Length; k++) mean[k] /= columns.Count;

        var mid = mean[100..400].Average();
        var high = mean[600..900].Average();
        await Assert.That(Math.Abs(mid - high) < 6.0).IsTrue(); // white noise should be roughly flat
    }

    [Test]
    public async Task Aggregation_RespectsMaxColumns()
    {
        var engine = new SpectrogramEngine(new SpectrogramSettings(WindowSize: 512, MaxColumns: 100));
        const int total = 512 * 600; // ~1199 windows at hop 256
        var columns = engine.Columns(Chunks(new float[total]), total, CancellationToken.None).ToList();
        await Assert.That(columns.Count).IsBetween(50, 101);
    }

    [Test]
    public async Task HopOverride_ProducesDenserColumns()
    {
        // (10240 - 512) / 64 + 1 = 153 windows at hop 64 (vs 39 at default hop 256)
        var settings = new SpectrogramSettings(WindowSize: 512, MaxColumns: 100_000, HopOverride: 64);
        var engine = new SpectrogramEngine(settings);
        const int total = 512 * 20;
        var count = engine.Columns(Chunks(new float[total]), 0, CancellationToken.None).Count();
        await Assert.That(count).IsBetween(145, 153);
    }

    [Test]
    public async Task Cancellation_StopsEnumeration()
    {
        var engine = new SpectrogramEngine(new SpectrogramSettings());
        using var cts = new CancellationTokenSource();

        static IEnumerable<float[]> Infinite()
        {
            while (true) yield return new float[8192];
        }

        await Assert.That(() =>
        {
            var n = 0;
            foreach (var _ in engine.Columns(Infinite(), 0, cts.Token))
                if (++n == 3) cts.Cancel();
        }).ThrowsExactly<OperationCanceledException>();
    }
}
