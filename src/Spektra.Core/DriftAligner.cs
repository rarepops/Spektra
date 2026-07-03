namespace Spektra.Core;

/// One local alignment measurement: the estimated lag (in samples) for the
/// segment centered at `CenterSample`, with its correlation confidence.
public readonly record struct DriftAnchor(int CenterSample, int Lag, double Confidence);

/// Piecewise alignment: instead of one global offset, split the two signals into
/// segments and cross-correlate each one, revealing how the lag changes across
/// the file (clock drift, edits, variable latency). Pure; operates on samples.
public static class DriftAligner
{
    public static IReadOnlyList<DriftAnchor> EstimateDrift(
        ReadOnlySpan<float> a, ReadOnlySpan<float> b, int segments, int maxLag)
    {
        segments = Math.Max(1, segments);
        var n = Math.Min(a.Length, b.Length);
        var segLen = n / segments;
        var anchors = new List<DriftAnchor>(segments);
        if (segLen < 8) return anchors;

        for (var s = 0; s < segments; s++)
        {
            var start = s * segLen;
            var len = s == segments - 1 ? n - start : segLen;
            if (len < 8) break;
            var (lag, conf) = Aligner.EstimateLag(
                a.Slice(start, len), b.Slice(start, len), Math.Min(maxLag, len - 1));
            anchors.Add(new DriftAnchor(start + len / 2, lag, conf));
        }
        return anchors;
    }

    /// Spread between the largest and smallest confident (>= minConfidence) lag,
    /// in samples. Near zero means a single global offset aligns the whole file;
    /// a large spread means the alignment drifts and piecewise warping is needed.
    public static int LagSpread(IReadOnlyList<DriftAnchor> anchors, double minConfidence = 0.4)
    {
        var min = int.MaxValue;
        var max = int.MinValue;
        foreach (var a in anchors)
            if (a.Confidence >= minConfidence)
            {
                if (a.Lag < min) min = a.Lag;
                if (a.Lag > max) max = a.Lag;
            }
        return max == int.MinValue ? 0 : max - min;
    }
}
