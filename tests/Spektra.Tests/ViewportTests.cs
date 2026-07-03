using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class ViewportTests
{
    [Fact]
    public void Defaults_CoverFullRanges()
    {
        var vp = new Viewport();
        Assert.Equal(0, vp.T0); Assert.Equal(1, vp.T1);
        Assert.Equal(0, vp.F0); Assert.Equal(1, vp.F1);
        Assert.False(vp.IsZoomed);
    }

    [Fact]
    public void ZoomTime_KeepsAnchorPointFixed()
    {
        var vp = new Viewport();
        vp.ZoomTime(0.25, 0.5); // pivot at absolute 0.25, new span 0.5
        Assert.Equal(0.125, vp.T0, 10);
        Assert.Equal(0.625, vp.T1, 10);
        Assert.True(vp.IsTimeZoomed);
        Assert.Equal(0.25, vp.T0 + 0.25 * vp.TimeSpanN, 10); // anchor invariant
    }

    [Fact]
    public void ZoomTime_OutClampsToFullRange()
    {
        var vp = new Viewport();
        vp.ZoomTime(0.5, 0.5);
        vp.ZoomTime(0.5, 10);
        Assert.Equal(0, vp.T0); Assert.Equal(1, vp.T1);
    }

    [Fact]
    public void ZoomTime_InClampsToMinSpan()
    {
        var vp = new Viewport { MinTimeSpan = 0.2 };
        vp.ZoomTime(0.5, 0.0001);
        Assert.Equal(0.2, vp.TimeSpanN, 10);
    }

    [Fact]
    public void PanTime_ClampsAtEdges()
    {
        var vp = new Viewport();
        vp.ZoomTime(0.5, 0.5);
        vp.PanTime(-999);
        Assert.Equal(0, vp.T0, 10);
        Assert.Equal(0.5, vp.TimeSpanN, 10);
    }

    [Fact]
    public void ZoomFrequency_AnchorTop_KeepsF1()
    {
        var vp = new Viewport();
        vp.ZoomFrequency(1.0, 0.5);
        Assert.Equal(0.5, vp.F0, 10); Assert.Equal(1, vp.F1, 10);
        Assert.Equal(0, vp.T0); Assert.Equal(1, vp.T1); // time untouched
    }

    [Fact]
    public void Reset_RestoresFullRanges()
    {
        var vp = new Viewport();
        vp.ZoomTime(0.5, 0.5); vp.ZoomFrequency(0.5, 0.5);
        vp.Reset();
        Assert.False(vp.IsZoomed);
    }

    [Fact]
    public void Changed_FiresOnRealChangesOnly()
    {
        var vp = new Viewport();
        var fired = 0;
        vp.Changed += () => fired++;
        vp.PanTime(0.5);        // full range: pan is a no-op
        Assert.Equal(0, fired);
        vp.ZoomTime(0.5, 0.5);  // real change
        Assert.Equal(1, fired);
        vp.Reset();
        Assert.Equal(2, fired);
        vp.Reset();             // already full: no-op
        Assert.Equal(2, fired);
    }
}
