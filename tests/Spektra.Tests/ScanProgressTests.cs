using Spektra.Core;

namespace Spektra.Tests;

public sealed class ScanProgressTests
{
    [Test]
    public async Task Fraction_IsByteWeighted_IncludingCachedBytes()
    {
        var p = new ScanProgress(TotalBytes: 1000, CachedBytes: 500, AnalyzedBytes: 250,
            AnalyzedFiles: 10, ElapsedSeconds: 10);
        await Assert.That(p.Fraction).IsCloseTo(0.75, 1e-9);
    }

    [Test]
    public async Task Fraction_IsOne_WhenTotalIsZero()
    {
        var p = new ScanProgress(0, 0, 0, 0, 0);
        await Assert.That(p.Fraction).IsCloseTo(1, 1e-9);
    }

    [Test]
    [Arguments(4, 10.0, 50L)]  // too few files
    [Arguments(5, 2.0, 50L)]   // too little time
    [Arguments(5, 10.0, 0L)]   // no analyzed bytes (zero-byte stubs)
    public async Task Eta_IsNull_BeforeTheEstimateMeansAnything(int files, double elapsed, long analyzed)
    {
        var p = new ScanProgress(TotalBytes: 1000, CachedBytes: 0, AnalyzedBytes: analyzed,
            AnalyzedFiles: files, ElapsedSeconds: elapsed);
        await Assert.That(p.EtaSeconds).IsNull();
    }

    [Test]
    public async Task Eta_ComesFromAnalyzedRate_CachedBytesExcluded()
    {
        // 50 MB analyzed in 10 s -> 5 MB/s; 100 MB left -> ~20 s.
        // The 850 MB of cached bytes must not inflate the rate.
        var p = new ScanProgress(
            TotalBytes: 1_000_000_000, CachedBytes: 850_000_000, AnalyzedBytes: 50_000_000,
            AnalyzedFiles: 20, ElapsedSeconds: 10);
        await Assert.That(p.EtaSeconds).IsNotNull();
        await Assert.That(p.EtaSeconds!.Value).IsCloseTo(20.0, 1e-6);
    }
}
