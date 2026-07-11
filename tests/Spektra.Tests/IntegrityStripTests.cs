using Spektra.Core;

namespace Spektra.Tests;

public sealed class IntegrityStripTests
{
    private static IntegrityReport Report(
        IReadOnlyList<DropoutRegion>? dropouts = null,
        double decoded = 10, double expected = 10, bool truncated = false) =>
        new(IntegrityStatus.Suspect, 0, dropouts ?? [], decoded, expected, truncated, "");

    [Test]
    public async Task FullView_MapsDropoutToItsFractions()
    {
        var report = Report([new DropoutRegion(2, 3)]);
        var segments = IntegrityStrip.Segments(report, durationSeconds: 10, t0: 0, t1: 1);
        await Assert.That(segments).HasSingleItem();
        var seg = segments.Single();
        await Assert.That(seg.X0).IsCloseTo(0.2, 1e-9);
        await Assert.That(seg.X1).IsCloseTo(0.3, 1e-9);
        await Assert.That(seg.MissingTail).IsFalse();
    }

    [Test]
    public async Task ZoomedWindow_DropsOutsideAndRescalesInside()
    {
        var report = Report([new DropoutRegion(1, 2), new DropoutRegion(6, 8)]);
        var segments = IntegrityStrip.Segments(report, 10, t0: 0.5, t1: 1.0);
        await Assert.That(segments).HasSingleItem();
        var seg = segments.Single();
        await Assert.That(seg.X0).IsCloseTo(0.2, 1e-9); // 6s -> (0.6-0.5)/0.5
        await Assert.That(seg.X1).IsCloseTo(0.6, 1e-9); // 8s -> (0.8-0.5)/0.5
    }

    [Test]
    public async Task Truncation_AddsAMissingTailSegment()
    {
        var report = Report(decoded: 6, expected: 10, truncated: true);
        var segments = IntegrityStrip.Segments(report, 10, 0, 1);
        await Assert.That(segments).HasSingleItem();
        var seg = segments.Single();
        await Assert.That(seg.X0).IsCloseTo(0.6, 1e-9);
        await Assert.That(seg.X1).IsCloseTo(1.0, 1e-9);
        await Assert.That(seg.MissingTail).IsTrue();
    }

    [Test]
    public async Task DropoutSpanningTheWindowEdge_ClampsToUnit()
    {
        var report = Report([new DropoutRegion(0, 6)]);
        var segments = IntegrityStrip.Segments(report, 10, t0: 0.25, t1: 0.75);
        await Assert.That(segments).HasSingleItem();
        var seg = segments.Single();
        await Assert.That(seg.X0).IsCloseTo(0.0, 1e-9);
        await Assert.That(seg.X1).IsCloseTo(0.7, 1e-9); // 6s -> (0.6-0.25)/0.5
    }

    [Test]
    public async Task CleanReport_IsEmpty() =>
        await Assert.That(IntegrityStrip.Segments(Report(), 10, 0, 1)).IsEmpty();

    [Test]
    public async Task ZeroDuration_IsEmpty() =>
        await Assert.That(IntegrityStrip.Segments(Report([new DropoutRegion(1, 2)]), 0, 0, 1)).IsEmpty();
}
