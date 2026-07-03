namespace Spektra.Core;

/// A stretch of missing/silent audio inside a file, in seconds.
public sealed record DropoutRegion(double StartSeconds, double EndSeconds)
{
    public double DurationSeconds => EndSeconds - StartSeconds;
}

/// Scans decoded mono samples for interior runs of (near-)zero data. Unfetched
/// torrent pieces and interrupted downloads decode to exact-zero PCM, and real
/// music is essentially never digitally silent mid-track, so a long interior gap
/// is a strong "missing data" signal. Leading and trailing silence (normal
/// lead-in / lead-out) is ignored. Pure and streaming.
public static class SilenceScanner
{
    public static IReadOnlyList<DropoutRegion> FindDropouts(
        IEnumerable<float[]> monoChunks, int sampleRate,
        double minGapSeconds = 0.5, float epsilon = 0f)
    {
        var result = new List<DropoutRegion>();
        if (sampleRate <= 0) return result;
        var minGap = (long)(minGapSeconds * sampleRate);

        long index = 0;       // global sample position
        long runStart = -1;   // start of the current silent run, or -1
        var started = false;  // have we seen any non-silent sample yet?

        foreach (var chunk in monoChunks)
            foreach (var sample in chunk)
            {
                var silent = MathF.Abs(sample) <= epsilon;
                if (silent)
                {
                    if (runStart < 0) runStart = index;
                }
                else
                {
                    // A silent run only counts as a dropout when it sits between
                    // real audio on both sides (so leading silence never counts).
                    if (started && runStart >= 0 && index - runStart >= minGap)
                        result.Add(new DropoutRegion(
                            (double)runStart / sampleRate, (double)index / sampleRate));
                    runStart = -1;
                    started = true;
                }
                index++;
            }

        // A trailing silent run is treated as lead-out and never recorded.
        return result;
    }
}
