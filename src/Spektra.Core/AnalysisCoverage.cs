namespace Spektra.Core;

/// How much of a folder has a verdict, for the folder tab's progress bar.
///
/// Deliberately not ScanProgress. That one measures progress through a single
/// run's worklist and answers 1 when the worklist is empty, which is correct
/// for a run and wrong for a folder: analysing ten stragglers in a hundred-file
/// folder sweeps a run bar from nothing to full, while the folder itself barely
/// changed. This answers what is known about the folder, so it means the same
/// thing whether or not anything is running, and reads 0 when there is nothing
/// to know rather than claiming completeness by vacuity.
public static class AnalysisCoverage
{
    public static double Fraction(int analyzed, int total) =>
        total <= 0 || analyzed <= 0 ? 0 : Math.Min(1, (double)analyzed / total);
}
