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
/// lead-in / lead-out) is ignored.
///
/// Streaming and incremental: Feed chunks as they decode, then read Dropouts and
/// SampleCount. The folder audit tees one decode through here and the spectrogram
/// engine at once; FindDropouts is the standalone one-shot form.
public sealed class SilenceScanner(int sampleRate, double minGapSeconds = 0.5, float epsilon = 0f)
{
    private readonly long _minGap = (long)(minGapSeconds * sampleRate);
    private readonly List<DropoutRegion> _dropouts = [];
    private long _index;         // global sample position / total samples seen
    private long _runStart = -1; // start of the current silent run, or -1
    private bool _started;       // have we seen any non-silent sample yet?

    /// Total samples fed so far; the integrity pass divides this by the sample
    /// rate to detect truncation.
    public long SampleCount => _index;

    /// Interior silent gaps found in what has been fed so far.
    public IReadOnlyList<DropoutRegion> Dropouts => _dropouts;

    public void Feed(float[] chunk)
    {
        // A non-positive rate can't map samples to seconds; keep counting so
        // SampleCount stays meaningful, but record no gaps.
        if (sampleRate <= 0) { _index += chunk.Length; return; }
        foreach (var sample in chunk)
        {
            if (MathF.Abs(sample) <= epsilon)
            {
                if (_runStart < 0) _runStart = _index;
            }
            else
            {
                // A silent run only counts as a dropout when it sits between
                // real audio on both sides (so leading silence never counts).
                if (_started && _runStart >= 0 && _index - _runStart >= _minGap)
                    _dropouts.Add(new DropoutRegion(
                        (double)_runStart / sampleRate, (double)_index / sampleRate));
                _runStart = -1;
                _started = true;
            }
            _index++;
        }
        // A trailing silent run is treated as lead-out and never recorded.
    }

    public static IReadOnlyList<DropoutRegion> FindDropouts(
        IEnumerable<float[]> monoChunks, int sampleRate,
        double minGapSeconds = 0.5, float epsilon = 0f)
    {
        if (sampleRate <= 0) return [];
        var scanner = new SilenceScanner(sampleRate, minGapSeconds, epsilon);
        foreach (var chunk in monoChunks) scanner.Feed(chunk);
        return scanner.Dropouts;
    }
}
