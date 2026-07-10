namespace Spektra.Core;

/// One horizontal segment of the integrity lane, in fractions of the visible
/// time window (0..1 across the plot). MissingTail marks the truncated region,
/// drawn dimmer than a dropout.
public sealed record StripSegment(double X0, double X1, bool MissingTail);

/// Maps integrity findings onto the spectrogram's visible viewport for the
/// lane under the plot. Pure math, kept in Core so it stays testable.
public static class IntegrityStrip
{
    /// `t0`/`t1` are the viewport's normalized time window (fractions of the
    /// full duration). Segments are clipped to the window; anything fully
    /// outside is dropped.
    public static IReadOnlyList<StripSegment> Segments(
        IntegrityReport report, double durationSeconds, double t0, double t1)
    {
        var result = new List<StripSegment>();
        if (durationSeconds <= 0 || t1 <= t0) return result;

        foreach (var d in report.Dropouts)
            Add(d.StartSeconds, d.EndSeconds, tail: false);
        if (report.Truncated && report.DecodedSeconds < durationSeconds)
            Add(report.DecodedSeconds, durationSeconds, tail: true);
        return result;

        void Add(double fromSeconds, double toSeconds, bool tail)
        {
            var x0 = (fromSeconds / durationSeconds - t0) / (t1 - t0);
            var x1 = (toSeconds / durationSeconds - t0) / (t1 - t0);
            if (x1 <= 0 || x0 >= 1) return;
            result.Add(new StripSegment(Math.Max(0, x0), Math.Min(1, x1), tail));
        }
    }
}
