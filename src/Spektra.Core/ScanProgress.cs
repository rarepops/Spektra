namespace Spektra.Core;

/// Byte-weighted folder-scan progress. Cached bytes count toward completion
/// but not toward the analysis rate the ETA is computed from; ElapsedSeconds
/// is measured over the analysis phase (after cache replay).
public sealed record ScanProgress(
    long TotalBytes, long CachedBytes, long AnalyzedBytes,
    int AnalyzedFiles, double ElapsedSeconds)
{
    public double Fraction => TotalBytes <= 0
        ? 1
        : Math.Min(1, (double)(CachedBytes + AnalyzedBytes) / TotalBytes);

    /// Null until the estimate means something: at least 5 analyzed files,
    /// 3 seconds elapsed, and at least one analyzed byte (zero-byte stub
    /// files would otherwise divide by zero).
    public double? EtaSeconds
    {
        get
        {
            if (AnalyzedFiles < 5 || ElapsedSeconds < 3 || AnalyzedBytes <= 0) return null;
            var remaining = TotalBytes - CachedBytes - AnalyzedBytes;
            if (remaining <= 0) return 0;
            return remaining / (AnalyzedBytes / ElapsedSeconds);
        }
    }
}
