namespace Spektra.Core;

/// Result of a time-domain null test (A minus B, sample by sample). A silent
/// residual means the two signals are identical over the span; a loud residual
/// is whatever differs. Values are dBFS.
public sealed record NullResult(float ResidualRmsDb, float ResidualPeakDb, float ReferenceRmsDb)
{
    /// How far the residual sits below the reference signal. Larger = closer to
    /// a perfect null (more identical).
    public float NullDepthDb => ReferenceRmsDb - ResidualRmsDb;

    public string Summary => ResidualRmsDb <= Db.Floor + 1f
        ? "Perfect null: identical samples over this span."
        : $"Residual {ResidualRmsDb:0.0} dB RMS ({NullDepthDb:0.0} dB below signal), peak {ResidualPeakDb:0.0} dB.";
}

/// Classic phase-cancellation test: decode both files to a common mono grid,
/// subtract sample by sample (B shifted by the alignment offset), and measure
/// what is left over.
public sealed class NullTest(FfmpegPaths ffmpeg)
{
    public NullResult Compute(
        string pathA, int? channelA, string pathB, int? channelB,
        double startSeconds, double durationSeconds, double offsetSeconds, int rate, CancellationToken ct)
    {
        var a = Decode(pathA, channelA, startSeconds, durationSeconds, rate, ct);
        var startB = Math.Max(0, startSeconds + offsetSeconds); // A_time + Offset, clamped >= 0
        var b = Decode(pathB, channelB, startB, durationSeconds, rate, ct);

        var n = Math.Min(a.Length, b.Length);
        double sumSq = 0, refSq = 0;
        var peak = 0f;
        for (var i = 0; i < n; i++)
        {
            var r = a[i] - b[i];
            sumSq += (double)r * r;
            refSq += (double)a[i] * a[i];
            var ar = MathF.Abs(r);
            if (ar > peak) peak = ar;
        }
        var rms = n > 0 ? (float)Math.Sqrt(sumSq / n) : 0f;
        var refRms = n > 0 ? (float)Math.Sqrt(refSq / n) : 0f;
        return new NullResult(Db.FromAmplitude(rms), Db.FromAmplitude(peak), Db.FromAmplitude(refRms));
    }

    private float[] Decode(string path, int? channel, double start, double dur, int rate, CancellationToken ct)
    {
        var decoder = new AudioDecoder(ffmpeg.FfmpegPath);
        var opts = new DecodeOptions(
            Start: TimeSpan.FromSeconds(start), Duration: TimeSpan.FromSeconds(dur),
            Channel: channel, SampleRate: rate);
        var list = new List<float>((int)(rate * dur) + rate);
        foreach (var chunk in decoder.DecodeMonoChunks(path, ct, opts)) list.AddRange(chunk);
        return [.. list];
    }
}
