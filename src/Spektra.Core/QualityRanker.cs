namespace Spektra.Core;

public sealed record QualityRanking(
    IReadOnlyList<string> Winners, string Confidence, string Reason, IReadOnlyList<string> OrderedPaths);

/// Ranks a duplicate group's members by a quality ladder built on audit facts,
/// and says how sure the ranking is. Ladder (see the spec): full-band lossless,
/// then lossless with the 20 kHz suspicious wall, then honest lossy by cutoff,
/// then proven transcodes and upsampled containers, then unjudgeable; corrupt
/// members drop to the bottom outright, suspect integrity costs one class.
public static class QualityRanker
{
    /// Members at least this similar and in the same class are the same audio:
    /// report a tie ("keep either") instead of inventing a winner.
    public const double IdenticalSimilarity = 0.98;

    private const double CutoffMarginHz = 1000.0;

    public static QualityRanking Rank(
        IReadOnlyList<(string Path, AuditRow Row)> members, Func<string, string, double> similarity)
    {
        var ordered = members
            .OrderByDescending(m => ClassOf(m.Row))
            .ThenByDescending(m => EffectiveCutoff(m.Row))
            .ThenByDescending(m => m.Row.BitrateBps ?? 0)
            .ThenByDescending(m => m.Row.SampleRateHz ?? 0)
            .ThenBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var top = ordered[0];
        var topClass = ClassOf(top.Row);
        var winners = ordered
            .TakeWhile(m => ClassOf(m.Row) == topClass
                && (m.Path == top.Path || similarity(top.Path, m.Path) >= IdenticalSimilarity))
            .Select(m => m.Path)
            .ToList();

        var runnerUp = ordered.Skip(winners.Count).FirstOrDefault();
        string confidence;
        string reason;
        if (runnerUp == default)
        {
            confidence = "High";
            reason = "identical copies, keep either";
        }
        else
        {
            var gap = topClass - ClassOf(runnerUp.Row);
            confidence = gap >= 2 ? "High"
                : gap == 1 ? "Medium"
                : Math.Abs(EffectiveCutoff(top.Row) - EffectiveCutoff(runnerUp.Row)) >= CutoffMarginHz ? "Medium"
                : "Low";
            reason = $"winner is {Describe(top.Row)}, runner-up is {Describe(runnerUp.Row)}";
        }

        if (topClass == HighWallClass) confidence = Cap(confidence, "Medium");
        if (top.Row.Integrity is "Suspect") confidence = Cap(confidence, "Medium");
        if (topClass <= 1) confidence = "Low";

        return new QualityRanking(winners, confidence, reason, [.. ordered.Select(m => m.Path)]);
    }

    private const int HighWallClass = 5;

    internal static int ClassOf(AuditRow row)
    {
        if (row.Integrity is "Corrupt" or "Error") return 0;
        var cls = TranscodeCheck.IsLosslessCodec(row.Codec)
            ? row.Bandwidth switch
            {
                "Lossless" => 6,
                "Suspicious" when row.CutoffHz >= CutoffAnalyzer.HighCutoffSuspicionHz => HighWallClass,
                "Suspicious" => 3, // gradual rolloff in a lossless container: rank with honest lossy
                "Lossy" or "Upsampled" => 2,
                _ => 1,
            }
            : row.Bandwidth switch
            {
                "Lossy" when TranscodeCheck.IsSuspectLossy(
                    row.Codec, row.BitrateBps, row.Channels, row.CutoffHz) => 2, // re-encoded lossy
                "Unknown" => 1,
                _ => 3, // an honest lossy file, whatever its verdict wording
            };
        return row.Integrity is "Suspect" ? Math.Max(1, cls - 1) : cls;
    }

    /// Null cutoff on a full-band verdict means "reaches the top": best. Null
    /// anywhere else means "nothing measured": worst.
    private static double EffectiveCutoff(AuditRow row) =>
        row.CutoffHz ?? (row.Bandwidth is "Lossless" ? double.MaxValue : 0);

    private static string Cap(string confidence, string cap) =>
        confidence == "High" && cap == "Medium" ? "Medium" : confidence;

    internal static string Describe(AuditRow row)
    {
        var codec = row.Codec ?? "unknown";
        var khz = row.CutoffHz is { } hz ? $"{hz / 1000.0:0.0} kHz" : "";
        var body = row.Bandwidth switch
        {
            "Lossless" => $"full-band lossless ({codec})",
            "Suspicious" when row.CutoffHz >= CutoffAnalyzer.HighCutoffSuspicionHz =>
                $"{codec} with a {khz} wall (high-bitrate lossy or band-limited master)",
            "Suspicious" => $"{codec} rolling off near {khz}",
            "Lossy" => $"{codec} cut at {khz}",
            "Upsampled" => $"upsampled {codec} (true bandwidth {khz})",
            _ => $"{codec} with unjudgeable bandwidth",
        };
        return row.Integrity switch
        {
            "Corrupt" or "Error" => body + " with integrity damage",
            "Suspect" => body + " worth an integrity listen",
            _ => body,
        };
    }
}
