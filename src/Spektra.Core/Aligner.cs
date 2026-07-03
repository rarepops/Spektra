using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Spektra.Core;

public readonly record struct AlignResult(TimeSpan Offset, double Confidence);

public static class Aligner
{
    /// Circular cross-correlation via FFT. Returns the integer lag L (|L| ≤ maxLag)
    /// maximizing sum_n a[n]*b[n+L], plus a normalized peak in [0,1]. A positive
    /// lag means b's content sits L samples LATER than a (b = a delayed by D → +D).
    public static (int Lag, double Confidence) EstimateLag(
        ReadOnlySpan<float> a, ReadOnlySpan<float> b, int maxLag)
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
        Fourier.Inverse(prod, FourierOptions.Matlab); // Matlab: 1/n on inverse → true correlation

        var bestLag = 0;
        var bestVal = float.NegativeInfinity;
        for (var l = -maxLag; l <= maxLag; l++)
        {
            var idx = ((l % n) + n) % n; // circular index handles negative lags
            var v = prod[idx].Real;
            if (v > bestVal) { bestVal = v; bestLag = l; }
        }
        var denom = Math.Sqrt(ea * eb);
        var conf = denom > 0 ? Math.Clamp(bestVal / denom, 0, 1) : 0;
        return (bestLag, conf);
    }

    /// Decodes the first `windowSeconds` of each file (mono, resampled to `rate`)
    /// and cross-correlates within ±`maxLagSeconds`. Offset follows the sign
    /// convention above: positive = B's content is later than A's.
    public static AlignResult EstimateOffset(
        FfmpegPaths ffmpeg, string pathA, string pathB, int? channelA, int? channelB,
        double windowSeconds = 10, int rate = 16000, double maxLagSeconds = 2,
        CancellationToken ct = default)
    {
        var a = DecodePrefix(ffmpeg, pathA, channelA, windowSeconds, rate, ct);
        var b = DecodePrefix(ffmpeg, pathB, channelB, windowSeconds, rate, ct);
        var (lag, conf) = EstimateLag(a, b, (int)(maxLagSeconds * rate));
        return new AlignResult(TimeSpan.FromSeconds((double)lag / rate), conf);
    }

    /// Decodes a longer window of each file and measures how much the alignment
    /// lag drifts across it (see DriftAligner). Returns the lag spread in seconds:
    /// near zero means one global offset aligns everything; larger means the two
    /// drift apart and a single offset cannot fully align them.
    public static double EstimateDriftSeconds(
        FfmpegPaths ffmpeg, string pathA, string pathB, int? channelA, int? channelB,
        double windowSeconds = 30, int rate = 16000, int segments = 6, double maxLagSeconds = 2,
        CancellationToken ct = default)
    {
        var a = DecodePrefix(ffmpeg, pathA, channelA, windowSeconds, rate, ct);
        var b = DecodePrefix(ffmpeg, pathB, channelB, windowSeconds, rate, ct);
        var anchors = DriftAligner.EstimateDrift(a, b, segments, (int)(maxLagSeconds * rate));
        return (double)DriftAligner.LagSpread(anchors) / rate;
    }

    private static float[] DecodePrefix(
        FfmpegPaths ffmpeg, string path, int? channel, double seconds, int rate, CancellationToken ct)
    {
        var decoder = new AudioDecoder(ffmpeg.FfmpegPath);
        var opts = new DecodeOptions(
            Duration: TimeSpan.FromSeconds(seconds), Channel: channel, SampleRate: rate);
        var list = new List<float>(rate * (int)Math.Ceiling(seconds));
        foreach (var chunk in decoder.DecodeMonoChunks(path, ct, opts)) list.AddRange(chunk);
        return [.. list];
    }
}

    