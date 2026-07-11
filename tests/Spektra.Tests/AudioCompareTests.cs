using Spektra.Core;

namespace Spektra.Tests;

public sealed class AudioCompareTests
{
    [Test]
    [Arguments(10, 10, 0, 0, 10)]   // aligned, equal length: full file
    [Arguments(10, 10, 2, 0, 8)]    // B later: overlap ends when B runs out
    [Arguments(10, 10, -2, 2, 8)]   // B earlier: overlap starts 2 s into A
    [Arguments(30, 10, 0, 0, 10)]   // B shorter than A
    public async Task OverlapSpan_ComputesAtimeWindow(
        double durA, double durB, double offset, double start, double duration)
    {
        var (s, d) = AudioCompare.OverlapSpan(durA, durB, offset);
        await Assert.That(s).IsCloseTo(start, 1e-9);
        await Assert.That(d).IsCloseTo(duration, 1e-9);
    }

    [Test]
    [Arguments(10, 3, 5)]    // B's 3 s sit entirely 5 s late: min(10, -2) < 0
    [Arguments(5, 10, -7)]   // B 7 s early: overlap would start at 7 s, past A's end
    public async Task OverlapSpan_DisjointFiles_HaveNoOverlap(
        double durA, double durB, double offset)
    {
        var (_, d) = AudioCompare.OverlapSpan(durA, durB, offset);
        await Assert.That(d <= 0).IsTrue();
    }

    private static readonly AudioMetadata Meta =
        new("flac", 44100, 2, 16, 1_040_000, TimeSpan.FromSeconds(231));

    private static CompareReport Report(
        float residualRmsDb, float referenceRmsDb, float thresholdDb = 40f,
        double? confidence = 0.9, double driftSeconds = 0) => new(
        "a.flac", "b.flac", Meta, Meta,
        OffsetSeconds: 0, AlignConfidence: confidence, DriftSeconds: driftSeconds,
        OverlapStartSeconds: 0, OverlapSeconds: 10,
        new DiffMetrics(0, 0, 0, 1),
        new NullResult(residualRmsDb, residualRmsDb, referenceRmsDb),
        thresholdDb);

    [Test]
    public async Task IsSame_WhenNullDepthMeetsThreshold() =>
        await Assert.That(Report(residualRmsDb: -60, referenceRmsDb: -14).IsSame).IsTrue(); // depth 46 >= 40

    [Test]
    public async Task Differs_WhenNullDepthBelowThreshold() =>
        await Assert.That(Report(residualRmsDb: -45, referenceRmsDb: -14).IsSame).IsFalse(); // depth 31 < 40

    [Test]
    public async Task IsSame_OnPerfectNull_DespiteZeroDepth() =>
        await Assert.That(Report(residualRmsDb: Db.Floor, referenceRmsDb: Db.Floor).IsSame).IsTrue(); // silence vs silence

    [Test]
    public async Task LowConfidence_OnlyBelowPointThree()
    {
        await Assert.That(Report(-60, -14, confidence: 0.29).LowConfidence).IsTrue();
        await Assert.That(Report(-60, -14, confidence: 0.3).LowConfidence).IsFalse();
        await Assert.That(Report(-60, -14, confidence: null).LowConfidence).IsFalse(); // pinned offset
    }

    [Test]
    public async Task HasDrift_OnlyAboveTwentyMs()
    {
        await Assert.That(Report(-60, -14, driftSeconds: 0.021).HasDrift).IsTrue();
        await Assert.That(Report(-60, -14, driftSeconds: 0.019).HasDrift).IsFalse();
    }

    [Test]
    public async Task ToCompareRow_MapsUnitsAndFileNames()
    {
        var report = new CompareReport(
            @"C:\music\a.flac", @"C:\music\b.mp3", Meta, Meta,
            OffsetSeconds: 0.012, AlignConfidence: 0.94, DriftSeconds: 0.005,
            OverlapStartSeconds: 0, OverlapSeconds: 231.5,
            new DiffMetrics(4.2f, 6.1f, 38f, 0.61),
            new NullResult(-44.9f, -31.2f, -14.2f),
            ThresholdDb: 40f);

        var row = Reporting.ToCompareRow(report);

        await Assert.That(row.FileA).IsEqualTo("a.flac");
        await Assert.That(row.FileB).IsEqualTo("b.mp3");
        await Assert.That(row.OffsetMs).IsCloseTo(12, 1e-6);
        await Assert.That(row.AlignConfidence!.Value).IsCloseTo(0.94, 1e-6);
        await Assert.That(row.DriftMs).IsCloseTo(5, 1e-6);
        await Assert.That(row.OverlapSeconds).IsCloseTo(231.5, 1e-6);
        await Assert.That(row.MeanAbsDb).IsCloseTo(4.2, 1e-5);
        await Assert.That(row.RmsDb).IsCloseTo(6.1, 1e-5);
        await Assert.That(row.MaxAbsDb).IsCloseTo(38, 1e-5);
        await Assert.That(row.WithinTolerancePct).IsCloseTo(61, 1e-5);
        await Assert.That(row.ResidualRmsDb).IsCloseTo(-44.9, 1e-5);
        await Assert.That(row.ResidualPeakDb).IsCloseTo(-31.2, 1e-5);
        await Assert.That(row.NullDepthDb).IsCloseTo(30.7, 1e-4); // -14.2 - (-44.9)
        await Assert.That(row.ThresholdDb).IsCloseTo(40, 1e-6);
        await Assert.That(row.Same).IsFalse(); // depth 30.7 < threshold 40
    }
}
