namespace Spektra.Core;

/// Computes a signed dB difference (A − B) on a common analysis grid: both files
/// decoded at the same sample rate (min of the two) with identical window/hop
/// over the same span, B shifted by offsetSeconds. Same rate ⇒ same bins ⇒
/// valid cell-by-cell subtraction; no per-bin resampling needed.
public sealed class SpectralDiff(FfmpegPaths ffmpeg)
{
    public IReadOnlyList<float[]> Compute(
        string pathA, AudioMetadata metaA, int? channelA,
        string pathB, AudioMetadata metaB, int? channelB,
        double startSeconds, double durationSeconds, double offsetSeconds,
        int targetColumns, int windowSize, WindowFunctionKind window, CancellationToken ct)
    {
        var rate = Math.Min(metaA.SampleRate, metaB.SampleRate);
        var spanSamples = (long)(durationSeconds * rate);
        var hop = (int)Math.Clamp(spanSamples / Math.Max(1, targetColumns), 64, 1024);
        var settings = new SpectrogramSettings(WindowSize: windowSize, MaxColumns: targetColumns, HopOverride: hop, Window: window);

        var colsA = AnalyzeSide(pathA, metaA, channelA, startSeconds, durationSeconds, rate, settings, ct);
        var startB = Math.Max(0, startSeconds + offsetSeconds); // A_time + Offset, clamped ≥ 0
        var colsB = AnalyzeSide(pathB, metaB, channelB, startB, durationSeconds, rate, settings, ct);

        var n = Math.Min(colsA.Count, colsB.Count);
        var bins = colsA.Count > 0 ? colsA[0].Length : settings.WindowSize / 2 + 1;
        var outCols = new List<float[]>(n);
        for (var c = 0; c < n; c++)
        {
            var d = new float[bins];
            for (var k = 0; k < bins; k++) d[k] = colsA[c][k] - colsB[c][k];
            outCols.Add(d);
        }
        return outCols;
    }

    private List<float[]> AnalyzeSide(
        string path, AudioMetadata meta, int? channel, double start, double dur, int rate,
        SpectrogramSettings settings, CancellationToken ct)
    {
        var session = new AnalysisSession(ffmpeg);
        var opts = new DecodeOptions(
            Start: TimeSpan.FromSeconds(start), Duration: TimeSpan.FromSeconds(dur),
            Channel: channel, SampleRate: rate);
        // AnalyzeColumns estimates column count from meta.SampleRate; we decode at
        // the common rate, so present metadata at that rate for a matching estimate.
        var metaAtRate = meta with { SampleRate = rate };
        return [.. session.AnalyzeColumns(path, metaAtRate, settings, ct, opts)];
    }
}
