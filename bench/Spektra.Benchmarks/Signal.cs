using Spektra.Core;

namespace Spektra.Benchmarks;

/// Deterministic, seeded synthetic inputs shared by the benchmarks. Every
/// generator is pure and reproducible, so a benchmark's GlobalSetup builds the
/// same data on every run and the numbers stay comparable over time.
internal static class Signal
{
    /// White noise in [-1, 1].
    public static float[] Noise(int n, int seed)
    {
        var rng = new Random(seed);
        var x = new float[n];
        for (var i = 0; i < n; i++) x[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
        return x;
    }

    /// A pure sine tone at freqHz.
    public static float[] Sine(int n, double freqHz, int sampleRate, double amp = 0.5)
    {
        var x = new float[n];
        var step = 2.0 * Math.PI * freqHz / sampleRate;
        for (var i = 0; i < n; i++) x[i] = (float)(amp * Math.Sin(step * i));
        return x;
    }

    /// Splits a signal into fixed-size chunks (the last one short), mimicking
    /// the streaming decoder output that SpectrogramEngine.Columns consumes.
    public static float[][] Chunk(float[] signal, int chunkSize)
    {
        var chunks = new List<float[]>((signal.Length + chunkSize - 1) / chunkSize);
        for (var off = 0; off < signal.Length; off += chunkSize)
        {
            var len = Math.Min(chunkSize, signal.Length - off);
            var c = new float[len];
            Array.Copy(signal, off, c, 0, len);
            chunks.Add(c);
        }
        return [.. chunks];
    }

    /// Returns b, a copy of a shifted later by delaySamples (zeros filled in at
    /// the front), same length as a. Gives the aligners a known positive lag.
    public static float[] Delay(float[] a, int delaySamples)
    {
        var b = new float[a.Length];
        for (var i = delaySamples; i < a.Length; i++) b[i] = a[i - delaySamples];
        return b;
    }

    /// dB columns shaped like a lossy brick-wall cutoff: bins up to cutoffHz sit
    /// near -20 dB with a little jitter, everything above collapses to the dB
    /// floor. This is the pattern CutoffAnalyzer scans for.
    public static float[][] BrickWallColumns(
        int columns, int bins, int sampleRate, double cutoffHz, int seed)
    {
        var rng = new Random(seed);
        var nyquist = sampleRate / 2.0;
        var hzPerBin = nyquist / (bins - 1);
        var cutoffBin = (int)(cutoffHz / hzPerBin);
        var result = new float[columns][];
        for (var c = 0; c < columns; c++)
        {
            var col = new float[bins];
            for (var k = 0; k < bins; k++)
                col[k] = k <= cutoffBin ? -20f + (float)(rng.NextDouble() * 6.0 - 3.0) : Db.Floor;
            result[c] = col;
        }
        return result;
    }
}
