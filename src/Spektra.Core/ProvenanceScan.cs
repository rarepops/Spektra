namespace Spektra.Core;

/// Provenance-aware bandwidth analysis. CutoffAnalyzer derives one cutoff from a
/// whole-file peak-hold, which a compilation, DJ mix, or continuous set can fool:
/// a genuinely lossless track fills the high bins a transcoded track left dead,
/// so the file inherits a clean verdict. This scans the file in fixed windows and,
/// when a transcode-suspect segment hides inside an otherwise-clean read, overrides
/// the whole-file verdict with Mixed. Honest lossy content never trips it: the
/// per-window suspect test is TranscodeCheck.IsSuspectLossy, the gate the row flag
/// already uses.
public static class ProvenanceScan
{
    // The scan window, and the shortest suspect run that triggers. Two windows
    // (about 60 seconds) means one misjudged window can never flip a file. Both
    // are generous defaults, to be calibrated against a verdict corpus later.
    private const double WindowSeconds = 30.0;
    private const int MinSuspectWindows = 2;

    public static LosslessVerdict Analyze(IReadOnlyList<float[]> columns, AudioMetadata meta)
    {
        var whole = CutoffAnalyzer.Analyze(columns, meta.SampleRate);

        // Mixed only rescues a clean-ish read of a long-enough file. A file
        // already flagged (Lossy/Upsampled) or unjudgeable (Unknown/Error) is
        // returned verbatim, as is one too short to be a real compilation.
        if (whole.Kind is not (VerdictKind.Lossless or VerdictKind.Suspicious)) return whole;
        var totalSeconds = meta.Duration.TotalSeconds;
        if (columns.Count == 0 || totalSeconds < 2 * WindowSeconds) return whole;

        var secondsPerColumn = totalSeconds / columns.Count;
        var windowCols = Math.Max(1, (int)Math.Round(WindowSeconds / secondsPerColumn));
        // Rounded, not truncated: the column count is rarely a whole number of
        // windows, and flooring folded the remainder into the last window. That
        // could swallow a two-window walled tail into one, putting a genuine
        // compilation back under the trigger. Rounding keeps a substantial
        // remainder as its own window and still absorbs a negligible one.
        var windowCount = Math.Max(1, (int)Math.Round((double)columns.Count / windowCols));

        var suspect = new bool[windowCount];
        var cutoffs = new double?[windowCount];
        for (var w = 0; w < windowCount; w++)
        {
            var start = w * windowCols;
            var end = w == windowCount - 1 ? columns.Count : start + windowCols;
            var v = CutoffAnalyzer.Analyze(Slice(columns, start, end), meta.SampleRate);
            cutoffs[w] = v.CutoffHz;
            suspect[w] = v.Kind is VerdictKind.Upsampled
                || (v.Kind is VerdictKind.Lossy
                    && TranscodeCheck.IsSuspectLossy(meta.Codec, meta.BitRateBps, meta.Channels, v.CutoffHz));
        }

        var (runStart, runLen) = LongestRun(suspect);
        if (runLen < MinSuspectWindows) return whole;

        // Worst (lowest) cutoff inside the triggering run is the most damning
        // evidence; the run's window span gives the reported time range.
        double? worst = null;
        for (var w = runStart; w < runStart + runLen; w++)
            if (cutoffs[w] is { } hz && (worst is null || hz < worst)) worst = hz;

        var from = TimeSpan.FromSeconds(runStart * WindowSeconds);
        var to = TimeSpan.FromSeconds(Math.Min(totalSeconds, (runStart + runLen) * WindowSeconds));
        var guess = worst is { } best ? CutoffAnalyzer.GuessCodec(best) : null;
        var summary =
            $"{whole.Kind} overall, but a lossy wall at {Khz(worst)} ({guess ?? "lossy"}) " +
            $"from {AudioMetadata.FormatDuration(from)} to {AudioMetadata.FormatDuration(to)}.";
        return new LosslessVerdict(VerdictKind.Mixed, worst, summary, guess);
    }

    private static float[][] Slice(IReadOnlyList<float[]> columns, int start, int end)
    {
        var slice = new float[end - start][];
        for (var i = start; i < end; i++) slice[i - start] = columns[i];
        return slice;
    }

    private static (int Start, int Len) LongestRun(bool[] flags)
    {
        int bestStart = 0, bestLen = 0, curStart = 0, curLen = 0;
        for (var i = 0; i < flags.Length; i++)
        {
            if (!flags[i]) { curLen = 0; continue; }
            if (curLen++ == 0) curStart = i;
            if (curLen > bestLen) { bestLen = curLen; bestStart = curStart; }
        }
        return (bestStart, bestLen);
    }

    private static string Khz(double? hz) => hz is { } v ? $"{v / 1000.0:0.0} kHz" : "unknown";
}
