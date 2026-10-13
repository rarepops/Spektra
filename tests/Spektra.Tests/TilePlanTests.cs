using Spektra.Core;

namespace Spektra.Tests;

public sealed class TilePlanTests
{
    // A 211.7 s file at 44.1 kHz, asked for 612 columns: nothing here is a
    // round multiple of anything else, so integer division shows.
    private static readonly TimeMap Whole = new(211.7);

    [Test]
    public async Task For_AMidZoom_SpreadsTheSpanAcrossTheColumns()
    {
        // 0.05 of 211.7 s is 10.585 s, 466798 samples, 762.74 per column:
        // the hop is cut to 762, where rounding would give 763.
        var plan = TilePlan.For(Whole, 0.31, 0.36, 44100, 612)!;
        await Assert.That(plan.Hop).IsEqualTo(762);
        await Assert.That(plan.Start.TotalSeconds).IsBetween(65.6269, 65.6271);
        await Assert.That(plan.Duration.TotalSeconds).IsBetween(10.5849, 10.5851);
    }

    [Test]
    public async Task For_ADeepZoom_NeverHopsBelow64Samples()
    {
        // 0.10585 s is 4667 samples, 7 per column unclamped.
        var plan = TilePlan.For(Whole, 0.5, 0.5005, 44100, 612)!;
        await Assert.That(plan.Hop).IsEqualTo(64);
    }

    [Test]
    public async Task For_TheWholeFile_NeverHopsAbove1024Samples()
    {
        var plan = TilePlan.For(Whole, 0, 1, 44100, 612)!;
        await Assert.That(plan.Hop).IsEqualTo(1024);
        await Assert.That(plan.Start).IsEqualTo(TimeSpan.Zero);
    }

    [Test]
    public async Task For_AShiftedFile_ClampsTheStartAtZero_AndKeepsTheSpan()
    {
        // The second file of a comparison, sitting 3.25 s earlier: the view
        // starts before its first sample.
        var plan = TilePlan.For(new TimeMap(211.7, ShiftSeconds: -3.25), 0.01, 0.06, 44100, 612)!;
        await Assert.That(plan.Start).IsEqualTo(TimeSpan.Zero);
        await Assert.That(plan.Duration.TotalSeconds).IsBetween(10.5849, 10.5851);
    }

    [Test]
    [Arguments(0.4, 0.4, 612)]
    [Arguments(0.5, 0.4, 612)]
    [Arguments(0.31, 0.36, 0)]
    public async Task For_NothingToDraw_IsNull(double t0, double t1, int columns)
    {
        await Assert.That(TilePlan.For(Whole, t0, t1, 44100, columns)).IsNull();
    }

    [Test]
    public async Task MinTimeSpan_IsWhere1024ColumnsReach64SamplesEach()
    {
        // 65536 samples out of 9335970.
        await Assert.That(TilePlan.MinTimeSpan(TimeSpan.FromSeconds(211.7), 44100))
            .IsBetween(0.0070197, 0.0070198);
    }

    [Test]
    public async Task MinTimeSpan_AFileShorterThanOneTile_CannotZoom()
    {
        await Assert.That(TilePlan.MinTimeSpan(TimeSpan.FromSeconds(1.2), 44100)).IsEqualTo(1.0);
    }

    [Test]
    public async Task MinTimeSpan_AnEmptyFile_CannotZoom()
    {
        await Assert.That(TilePlan.MinTimeSpan(TimeSpan.Zero, 44100)).IsEqualTo(1.0);
    }
}
