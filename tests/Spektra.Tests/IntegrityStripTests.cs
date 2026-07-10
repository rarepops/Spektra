using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public sealed class IntegrityStripTests
{
    private static IntegrityReport Report(
        IReadOnlyList<DropoutRegion>? dropouts = null,
        double decoded = 10, double expected = 10, bool truncated = false) =>
        new(IntegrityStatus.Suspect, 0, dropouts ?? [], decoded, expected, truncated, "");

    [Fact]
    public void FullView_MapsDropoutToItsFractions()
    {
        var report = Report([new DropoutRegion(2, 3)]);
        var seg = Assert.Single(IntegrityStrip.Segments(report, durationSeconds: 10, t0: 0, t1: 1));
        Assert.Equal(0.2, seg.X0, precision: 9);
        Assert.Equal(0.3, seg.X1, precision: 9);
        Assert.False(seg.MissingTail);
    }

    [Fact]
    public void ZoomedWindow_DropsOutsideAndRescalesInside()
    {
        var report = Report([new DropoutRegion(1, 2), new DropoutRegion(6, 8)]);
        var seg = Assert.Single(IntegrityStrip.Segments(report, 10, t0: 0.5, t1: 1.0));
        Assert.Equal(0.2, seg.X0, precision: 9); // 6s -> (0.6-0.5)/0.5
        Assert.Equal(0.6, seg.X1, precision: 9); // 8s -> (0.8-0.5)/0.5
    }

    [Fact]
    public void Truncation_AddsAMissingTailSegment()
    {
        var report = Report(decoded: 6, expected: 10, truncated: true);
        var seg = Assert.Single(IntegrityStrip.Segments(report, 10, 0, 1));
        Assert.Equal(0.6, seg.X0, precision: 9);
        Assert.Equal(1.0, seg.X1, precision: 9);
        Assert.True(seg.MissingTail);
    }

    [Fact]
    public void DropoutSpanningTheWindowEdge_ClampsToUnit()
    {
        var report = Report([new DropoutRegion(0, 6)]);
        var seg = Assert.Single(IntegrityStrip.Segments(report, 10, t0: 0.25, t1: 0.75));
        Assert.Equal(0.0, seg.X0, precision: 9);
        Assert.Equal(0.7, seg.X1, precision: 9); // 6s -> (0.6-0.25)/0.5
    }

    [Fact]
    public void CleanReport_IsEmpty() =>
        Assert.Empty(IntegrityStrip.Segments(Report(), 10, 0, 1));

    [Fact]
    public void ZeroDuration_IsEmpty() =>
        Assert.Empty(IntegrityStrip.Segments(Report([new DropoutRegion(1, 2)]), 0, 0, 1));
}
