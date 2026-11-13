namespace Spektra.Core;

/// How the first seconds of a suspect pair's two copies line up. Declared in the
/// order the window lists them: the top tier (doesn't line up, can't compare),
/// then the dimmed bottom tier (partly the same, same performance).
///
/// Alignment can clear a pair and never convict one. "Doesn't line up" is what a
/// cover, another song, a heavy remaster, a different intro and an offset beyond
/// the search all read as, so no label says that a copy is fake.
public enum SuspectLabel { DoesntLineUp, CantCompare, PartlySame, SamePerformance }

/// A pair's label and the alignment confidence behind it, in [0, 1]. The
/// confidence is null for CantCompare: no number stands behind it.
public sealed record SuspectVerdict(SuspectLabel Label, double? Confidence);

/// One pair finished during a LabelAll run: its index in the list, its verdict,
/// and how many pairs are done so far (this one included) out of the total.
public readonly record struct LabelProgress(int Index, SuspectVerdict Verdict, int Done, int Total);

/// Labels each suspect pair (two same-named files whose audio groups disagree) by
/// how well the first 10 s of its two copies line up. It runs the cross-correlation
/// that `spektra-cli diff` runs, so a confidence here is the number diff reports.
public static class SuspectLabeler
{
    /// How much of each copy is compared: its first 10 s. This and the next two
    /// constants are Aligner.EstimateOffset's own defaults, which spektra-cli diff
    /// uses, so the 2026-11-12 calibration (185 real pairs, measured by hand)
    /// transfers as is.
    public const double WindowSeconds = 10;

    /// The rate both windows are decoded at, mono.
    public const int SampleRate = 16000;

    /// How far apart the two copies may start and still line up: 2 s either way.
    /// A copy that starts later than that, or opens with a different intro, reads
    /// as not lining up.
    public const double MaxLagSeconds = 2;

    /// Below this confidence a pair does not line up: a cover, another song or a
    /// heavy remaster. In the calibration a cover read 0.17, unrelated songs about
    /// 0.03 and heavy remasters as low as 0.09, while another mix or edit of the
    /// same production started at 0.45.
    public const double LinesUpFrom = 0.45;

    /// From this confidence a pair is the same performance, another master or an
    /// identical copy (the calibration's median was 0.925). The readings between
    /// LinesUpFrom and here, 0.45 to 0.65 in the calibration, were the same
    /// production in another mix or edit.
    public const double SamePerformanceFrom = 0.80;

    // A tenth of a second of slack under the window: the resampler can land a few
    // samples short of 10 s on a copy that is long enough.
    private const int MinSamples = (int)((WindowSeconds - 0.1) * SampleRate);

    private const int MaxLagSamples = (int)(MaxLagSeconds * SampleRate);

    /// The label a confidence earns: below LinesUpFrom a pair does not line up,
    /// below SamePerformanceFrom it is partly the same, from there on it is the
    /// same performance. Never CantCompare: that comes from a copy that cannot be
    /// read, not from a number. Tested from the top so that a NaN lands in
    /// "doesn't line up", never in "same performance".
    public static SuspectLabel FromConfidence(double confidence) =>
        confidence >= SamePerformanceFrom ? SuspectLabel.SamePerformance
        : confidence >= LinesUpFrom ? SuspectLabel.PartlySame
        : SuspectLabel.DoesntLineUp;

    /// Labels one pair: decodes the first WindowSeconds of each copy (mono, at
    /// SampleRate) and cross-correlates them within MaxLagSeconds either way, as
    /// `spektra-cli diff` does. The confidence is the height of that peak.
    ///
    /// Alignment can clear a pair and never convict one. A high confidence says
    /// the two lined up in those seconds, not that nothing else differs, and a low
    /// one says they did not, not that either file is fake.
    ///
    /// A copy that cannot be decoded (corrupt, deleted, on an unplugged drive) or
    /// that decodes to less than the window gives CantCompare, with no confidence.
    /// A cancelled token throws OperationCanceledException instead, even when the
    /// cancel is what cut a decode short: "not checked" is never "can't compare".
    public static SuspectVerdict Label(
        FfmpegPaths ffmpeg, string pathA, string pathB, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        float[] a, b;
        try
        {
            a = Aligner.DecodePrefix(ffmpeg, pathA, null, WindowSeconds, SampleRate, ct);
            b = Aligner.DecodePrefix(ffmpeg, pathB, null, WindowSeconds, SampleRate, ct);
        }
        catch (Exception e) when (e is AudioDecodeException or IOException)
        {
            return CantCompare(ct);
        }
        if (a.Length < MinSamples || b.Length < MinSamples) return CantCompare(ct);

        var (_, confidence) = Aligner.EstimateLag(a, b, MaxLagSamples);
        return new SuspectVerdict(FromConfidence(confidence), confidence);
    }

    /// Labels every pair, up to `jobs` at a time. Each pair is reported once, as
    /// it finishes, with the running count, and the verdicts come back in pair
    /// order whatever order they finished in. A cancelled token throws
    /// OperationCanceledException and the ffmpeg work stops with it; the pairs
    /// that finished before the cancel were already reported.
    public static SuspectVerdict[] LabelAll(
        FfmpegPaths ffmpeg, IReadOnlyList<SuspectPair> pairs, int jobs,
        IProgress<LabelProgress>? progress = null, CancellationToken ct = default)
    {
        var verdicts = new SuspectVerdict[pairs.Count];
        var done = 0;
        var gate = new object();
        Parallel.For(0, pairs.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs), CancellationToken = ct },
            i =>
            {
                var verdict = Label(ffmpeg, pairs[i].PathA, pairs[i].PathB, ct);
                verdicts[i] = verdict;
                // Counted and reported under one lock, so the reports arrive in the
                // order of their counts.
                lock (gate) progress?.Report(new LabelProgress(i, verdict, ++done, pairs.Count));
            });
        return verdicts;
    }

    // A decode that fails or comes back short because the token was cancelled
    // (ffmpeg killed mid-decode, say) is a cancel, not a copy that cannot be
    // compared.
    private static SuspectVerdict CantCompare(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return new SuspectVerdict(SuspectLabel.CantCompare, null);
    }
}
