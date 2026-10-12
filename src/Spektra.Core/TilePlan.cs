namespace Spektra.Core;

/// The decode a sharp zoom tile needs: which span of the file, and how many
/// samples each column steps.
public sealed record TilePlan(TimeSpan Start, TimeSpan Duration, int Hop)
{
    /// A tile never resolves finer than this many samples per column, and
    /// never coarser than MaxHop.
    private const int MinHop = 64;
    private const int MaxHop = 1024;

    /// The width zoom is limited against. Unrelated to MaxHop, which happens
    /// to share the number.
    private const int NominalColumns = 1024;

    /// The plan for showing the normalized span t0..t1 across targetColumns,
    /// or null when that span holds no samples or no columns were asked for.
    public static TilePlan? For(TimeMap map, double t0, double t1, int sampleRate, int targetColumns)
    {
        var startSec = Math.Max(0, map.StartSeconds(t0));
        var spanSec = map.DurationSeconds(t1 - t0);
        var spanSamples = (long)(spanSec * sampleRate);
        if (spanSamples <= 0 || targetColumns <= 0) return null;

        var hop = (int)Math.Clamp(spanSamples / targetColumns, MinHop, MaxHop);
        return new TilePlan(TimeSpan.FromSeconds(startSec), TimeSpan.FromSeconds(spanSec), hop);
    }

    /// The narrowest normalized time span worth zooming to: a nominal
    /// 1024-column tile never drops below 64 samples per hop.
    public static double MinTimeSpan(TimeSpan duration, int sampleRate)
    {
        var totalSamples = duration.TotalSeconds * sampleRate;
        return totalSamples > 0 ? Math.Min(1, (double)MinHop * NominalColumns / totalSamples) : 1;
    }
}
