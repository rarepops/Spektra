using Spektra.Core;
using Xunit;

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

    [Fact]
    public void Sine_ProducesPeakInCorrectBin_EveryColumn()
    {
        var engine = new SpectrogramEngine(new SpectrogramSettings());
        var samples = Sine(1000f, 44100, 44100 * 3);
        var columns = engine.Columns(Chunks(samples), samples.Length, CancellationToken.None).ToList();

        // (132300 - 2048) / 1024 + 1 = 128 windows
        Assert.InRange(columns.Count, 120, 130);
        Assert.All(columns, c => Assert.Equal(1025, c.Length));
        foreach (var col in columns)
        {
            var peak = 0;
            for (var k = 1; k < col.Length; k++) if (col[k] > col[peak]) peak = k;
            Assert.InRange(peak, 46, 47);
            Assert.True(col[peak] > -2f);
        }
    }

    [Fact]
    public void Chirp_PeakBinRises()
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
        Assert.True(ArgMax(columns[^10]) > ArgMax(columns[5]) + 400,
            $"chirp should rise: early={ArgMax(columns[5])} late={ArgMax(columns[^10])}");
    }

    [Fact]
    public void WhiteNoise_SpectrumIsApproximatelyFlat()
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
        Assert.True(Math.Abs(mid - high) < 6.0,
            $"white noise should be roughly flat: mid={mid:0.0} dB, high={high:0.0} dB");
    }

    [Fact]
    public void Aggregation_RespectsMaxColumns()
    {
        var engine = new SpectrogramEngine(new SpectrogramSettings(WindowSize: 512, MaxColumns: 100));
        const int total = 512 * 600; // ~1199 windows at hop 256
        var columns = engine.Columns(Chunks(new float[total]), total, CancellationToken.None).ToList();
        Assert.InRange(columns.Count, 50, 101);
    }

    [Fact]
    public void Cancellation_StopsEnumeration()
    {
        var engine = new SpectrogramEngine(new SpectrogramSettings());
        using var cts = new CancellationTokenSource();

        static IEnumerable<float[]> Infinite()
        {
            while (true) yield return new float[8192];
        }

        Assert.Throws<OperationCanceledException>(() =>
        {
            var n = 0;
            foreach (var _ in engine.Columns(Infinite(), 0, cts.Token))
                if (++n == 3) cts.Cancel();
        });
    }
}
