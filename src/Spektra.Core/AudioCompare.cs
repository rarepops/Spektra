namespace Spektra.Core;

/// Options for a two-file comparison. OffsetSeconds null means auto-align;
/// the analysis parameters default to the desktop app's compare defaults.
public sealed record CompareOptions(
    double? OffsetSeconds = null,
    float ThresholdDb = 40f,
    int TargetColumns = 1024,
    int WindowSize = 2048,
    WindowFunctionKind Window = WindowFunctionKind.Hann);

/// Everything one A/B comparison reports: alignment, drift, the overlap
/// window that was analyzed, spectral diff metrics, and the null test.
public sealed record CompareReport(
    string PathA, string PathB,
    AudioMetadata MetaA, AudioMetadata MetaB,
    double OffsetSeconds, double? AlignConfidence, double DriftSeconds,
    double OverlapStartSeconds, double OverlapSeconds,
    DiffMetrics Diff, NullResult Null, float ThresholdDb)
{
    /// Perfect-null escape hatch first: two identical silent files have a
    /// floor-level residual but ~0 dB null depth, and must count as same.
    public bool IsSame =>
        Null.ResidualRmsDb <= Db.Floor + 1f || Null.NullDepthDb >= ThresholdDb;

    public bool LowConfidence => AlignConfidence is < 0.3;
    public bool HasDrift => DriftSeconds > 0.02;
}

/// Runs the GUI compare pipeline headlessly over two files. Run() is thin
/// orchestration of the existing primitives; the pure parts live in statics.
public sealed class AudioCompare(FfmpegPaths ffmpeg)
{
    /// Metadata, alignment (unless pinned), drift estimate, then a spectral
    /// diff and a null test over the files' overlapping span. Channels are
    /// null throughout: mono mixdown, the GUI compare default. Throws
    /// AudioDecodeException when the files do not overlap at the offset.
    public CompareReport Run(
        string pathA, string pathB, CompareOptions options, CancellationToken ct = default)
    {
        var session = new AnalysisSession(ffmpeg);
        var metaA = session.ReadMetadata(pathA);
        var metaB = session.ReadMetadata(pathB);

        double offset;
        double? confidence;
        if (options.OffsetSeconds is { } pinned) { offset = pinned; confidence = null; }
        else
        {
            var res = Aligner.EstimateOffset(ffmpeg, pathA, pathB, null, null, ct: ct);
            offset = res.Offset.TotalSeconds;
            confidence = res.Confidence;
        }

        var drift = Aligner.EstimateDriftSeconds(ffmpeg, pathA, pathB, null, null, ct: ct);

        var (start, duration) = OverlapSpan(
            metaA.Duration.TotalSeconds, metaB.Duration.TotalSeconds, offset);
        if (duration <= 0)
            throw new AudioDecodeException(
                "The files do not overlap at this offset; nothing to compare.");

        var cols = new SpectralDiff(ffmpeg).Compute(
            pathA, metaA, null, pathB, metaB, null,
            start, duration, offset, options.TargetColumns, options.WindowSize,
            options.Window, ct);
        var diff = DiffScore.Compute(cols);

        var rate = Math.Min(metaA.SampleRate, metaB.SampleRate);
        var nul = new NullTest(ffmpeg).Compute(
            pathA, null, pathB, null, start, duration, offset, rate, ct);

        return new CompareReport(
            pathA, pathB, metaA, metaB, offset, confidence, drift,
            start, duration, diff, nul, options.ThresholdDb);
    }

    /// A-time overlap window of the two files when B is shifted by
    /// offsetSeconds (positive = B's content is later than A's).
    /// A returned Duration <= 0 means the files do not overlap.
    public static (double Start, double Duration) OverlapSpan(
        double durationA, double durationB, double offsetSeconds)
    {
        var start = Math.Max(0, -offsetSeconds);
        var end = Math.Min(durationA, durationB - offsetSeconds);
        return (start, end - start);
    }
}
