namespace Spektra.Core;

/// Single-number summaries of a signed A-B spectral difference, reducing a whole
/// diff to numbers you can read at a glance. All values are in dB.
public sealed record DiffMetrics(
    float MeanAbsDb, float RmsDb, float MaxAbsDb, double FractionWithinTolerance)
{
    public string Summary =>
        $"mean |Δ| {MeanAbsDb:0.0} dB · RMS {RmsDb:0.0} dB · max {MaxAbsDb:0.0} dB · " +
        $"{FractionWithinTolerance * 100:0}% within tolerance";
}

public static class DiffScore
{
    /// Aggregates the per-cell dB differences. `toleranceDb` sets what counts as
    /// "effectively identical" for the within-tolerance fraction.
    public static DiffMetrics Compute(IReadOnlyList<float[]> diffColumns, float toleranceDb = 3f)
    {
        double sumAbs = 0, sumSq = 0;
        float maxAbs = 0;
        long n = 0, within = 0;
        foreach (var col in diffColumns)
            foreach (var d in col)
            {
                var a = MathF.Abs(d);
                sumAbs += a;
                sumSq += (double)d * d;
                if (a > maxAbs) maxAbs = a;
                if (a <= toleranceDb) within++;
                n++;
            }
        if (n == 0) return new DiffMetrics(0, 0, 0, 1);
        return new DiffMetrics(
            (float)(sumAbs / n), (float)Math.Sqrt(sumSq / n), maxAbs, (double)within / n);
    }
}
