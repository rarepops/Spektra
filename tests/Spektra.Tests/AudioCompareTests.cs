using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public sealed class AudioCompareTests
{
    [Theory]
    [InlineData(10, 10, 0, 0, 10)]   // aligned, equal length: full file
    [InlineData(10, 10, 2, 0, 8)]    // B later: overlap ends when B runs out
    [InlineData(10, 10, -2, 2, 8)]   // B earlier: overlap starts 2 s into A
    [InlineData(30, 10, 0, 0, 10)]   // B shorter than A
    public void OverlapSpan_ComputesAtimeWindow(
        double durA, double durB, double offset, double start, double duration)
    {
        var (s, d) = AudioCompare.OverlapSpan(durA, durB, offset);
        Assert.Equal(start, s, precision: 9);
        Assert.Equal(duration, d, precision: 9);
    }

    [Theory]
    [InlineData(10, 3, 5)]    // B's 3 s sit entirely 5 s late: min(10, -2) < 0
    [InlineData(5, 10, -7)]   // B 7 s early: overlap would start at 7 s, past A's end
    public void OverlapSpan_DisjointFiles_HaveNoOverlap(
        double durA, double durB, double offset)
    {
        var (_, d) = AudioCompare.OverlapSpan(durA, durB, offset);
        Assert.True(d <= 0);
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

    [Fact]
    public void IsSame_WhenNullDepthMeetsThreshold() =>
        Assert.True(Report(residualRmsDb: -60, referenceRmsDb: -14).IsSame); // depth 46 >= 40

    [Fact]
    public void Differs_WhenNullDepthBelowThreshold() =>
        Assert.False(Report(residualRmsDb: -45, referenceRmsDb: -14).IsSame); // depth 31 < 40

    [Fact]
    public void IsSame_OnPerfectNull_DespiteZeroDepth() =>
        Assert.True(Report(residualRmsDb: Db.Floor, referenceRmsDb: Db.Floor).IsSame); // silence vs silence

    [Fact]
    public void LowConfidence_OnlyBelowPointThree()
    {
        Assert.True(Report(-60, -14, confidence: 0.29).LowConfidence);
        Assert.False(Report(-60, -14, confidence: 0.3).LowConfidence);
        Assert.False(Report(-60, -14, confidence: null).LowConfidence); // pinned offset
    }

    [Fact]
    public void HasDrift_OnlyAboveTwentyMs()
    {
        Assert.True(Report(-60, -14, driftSeconds: 0.021).HasDrift);
        Assert.False(Report(-60, -14, driftSeconds: 0.019).HasDrift);
    }
}
