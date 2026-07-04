namespace Spektra.Core;

/// Time-domain dynamics computed straight from the samples: sample peak, RMS,
/// crest factor (peak over RMS, a simple dynamic-range proxy), and how many
/// samples sit at or near full scale (a clipping hint). All levels are dBFS.
public readonly record struct DynamicsStats(
    float PeakDbfs, float RmsDbfs, float CrestFactorDb, long ClippedSamples, long TotalSamples)
{
    public double ClippedFraction => TotalSamples > 0 ? (double)ClippedSamples / TotalSamples : 0;
}

public static class Dynamics
{
    public static DynamicsStats FromSamples(
        IEnumerable<float[]> monoChunks, float clipThreshold = 0.9997f)
    {
        double sumSq = 0;
        var peak = 0f;
        long clipped = 0, total = 0;
        foreach (var chunk in monoChunks)
            foreach (var s in chunk)
            {
                var a = MathF.Abs(s);
                if (a > peak) peak = a;
                sumSq += (double)s * s;
                if (a >= clipThreshold) clipped++;
                total++;
            }

        var rms = total > 0 ? (float)Math.Sqrt(sumSq / total) : 0f;
        var peakDb = Db.FromAmplitude(peak);
        var rmsDb = Db.FromAmplitude(rms);
        return new DynamicsStats(peakDb, rmsDb, peakDb - rmsDb, clipped, total);
    }
}
