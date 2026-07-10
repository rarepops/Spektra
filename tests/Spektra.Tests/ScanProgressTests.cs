using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public sealed class ScanProgressTests
{
    [Fact]
    public void Fraction_IsByteWeighted_IncludingCachedBytes()
    {
        var p = new ScanProgress(TotalBytes: 1000, CachedBytes: 500, AnalyzedBytes: 250,
            AnalyzedFiles: 10, ElapsedSeconds: 10);
        Assert.Equal(0.75, p.Fraction, precision: 9);
    }

    [Fact]
    public void Fraction_IsOne_WhenTotalIsZero()
    {
        var p = new ScanProgress(0, 0, 0, 0, 0);
        Assert.Equal(1, p.Fraction, precision: 9);
    }

    [Theory]
    [InlineData(4, 10.0, 50L)]  // too few files
    [InlineData(5, 2.0, 50L)]   // too little time
    [InlineData(5, 10.0, 0L)]   // no analyzed bytes (zero-byte stubs)
    public void Eta_IsNull_BeforeTheEstimateMeansAnything(int files, double elapsed, long analyzed)
    {
        var p = new ScanProgress(TotalBytes: 1000, CachedBytes: 0, AnalyzedBytes: analyzed,
            AnalyzedFiles: files, ElapsedSeconds: elapsed);
        Assert.Null(p.EtaSeconds);
    }

    [Fact]
    public void Eta_ComesFromAnalyzedRate_CachedBytesExcluded()
    {
        // 50 MB analyzed in 10 s -> 5 MB/s; 100 MB left -> ~20 s.
        // The 850 MB of cached bytes must not inflate the rate.
        var p = new ScanProgress(
            TotalBytes: 1_000_000_000, CachedBytes: 850_000_000, AnalyzedBytes: 50_000_000,
            AnalyzedFiles: 20, ElapsedSeconds: 10);
        Assert.NotNull(p.EtaSeconds);
        Assert.Equal(20.0, p.EtaSeconds!.Value, precision: 6);
    }
}
