using Spektra.Core;

namespace Spektra.Tests;

public class ViewportTests
{
    [Test]
    public async Task Defaults_CoverFullRanges()
    {
        var vp = new Viewport();
        await Assert.That(vp.T0).IsEqualTo(0); await Assert.That(vp.T1).IsEqualTo(1);
        await Assert.That(vp.F0).IsEqualTo(0); await Assert.That(vp.F1).IsEqualTo(1);
        await Assert.That(vp.IsZoomed).IsFalse();
    }

    [Test]
    public async Task ZoomTime_KeepsAnchorPointFixed()
    {
        var vp = new Viewport();
        vp.ZoomTime(0.25, 0.5); // pivot at absolute 0.25, new span 0.5
        await Assert.That(vp.T0).IsCloseTo(0.125, 1e-10);
        await Assert.That(vp.T1).IsCloseTo(0.625, 1e-10);
        await Assert.That(vp.IsTimeZoomed).IsTrue();
        await Assert.That(vp.T0 + 0.25 * vp.TimeSpanN).IsCloseTo(0.25, 1e-10); // anchor invariant
    }

    [Test]
    public async Task ZoomTime_OutClampsToFullRange()
    {
        var vp = new Viewport();
        vp.ZoomTime(0.5, 0.5);
        vp.ZoomTime(0.5, 10);
        await Assert.That(vp.T0).IsEqualTo(0); await Assert.That(vp.T1).IsEqualTo(1);
    }

    [Test]
    public async Task ZoomTime_InClampsToMinSpan()
    {
        var vp = new Viewport { MinTimeSpan = 0.2 };
        vp.ZoomTime(0.5, 0.0001);
        await Assert.That(vp.TimeSpanN).IsCloseTo(0.2, 1e-10);
    }

    [Test]
    public async Task PanTime_ClampsAtEdges()
    {
        var vp = new Viewport();
        vp.ZoomTime(0.5, 0.5);
        vp.PanTime(-999);
        await Assert.That(vp.T0).IsCloseTo(0, 1e-10);
        await Assert.That(vp.TimeSpanN).IsCloseTo(0.5, 1e-10);
    }

    [Test]
    public async Task ZoomFrequency_AnchorTop_KeepsF1()
    {
        var vp = new Viewport();
        vp.ZoomFrequency(1.0, 0.5);
        await Assert.That(vp.F0).IsCloseTo(0.5, 1e-10); await Assert.That(vp.F1).IsCloseTo(1, 1e-10);
        await Assert.That(vp.T0).IsEqualTo(0); await Assert.That(vp.T1).IsEqualTo(1); // time untouched
    }

    [Test]
    public async Task Reset_RestoresFullRanges()
    {
        var vp = new Viewport();
        vp.ZoomTime(0.5, 0.5); vp.ZoomFrequency(0.5, 0.5);
        vp.Reset();
        await Assert.That(vp.IsZoomed).IsFalse();
    }

    [Test]
    public async Task Changed_FiresOnRealChangesOnly()
    {
        var vp = new Viewport();
        var fired = 0;
        vp.Changed += () => fired++;
        vp.PanTime(0.5);        // full range: pan is a no-op
        await Assert.That(fired).IsEqualTo(0);
        vp.ZoomTime(0.5, 0.5);  // real change
        await Assert.That(fired).IsEqualTo(1);
        vp.Reset();
        await Assert.That(fired).IsEqualTo(2);
        vp.Reset();             // already full: no-op
        await Assert.That(fired).IsEqualTo(2);
    }
}
