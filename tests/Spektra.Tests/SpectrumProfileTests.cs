using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class SpectrumProfileTests
{
    [Fact]
    public void Average_IsPerBinMean()
    {
        var avg = SpectrumProfile.Average([[-20f, -60f, -100f], [-40f, -20f, -100f]]);
        Assert.Equal(-30f, avg[0], 3);
        Assert.Equal(-40f, avg[1], 3);
        Assert.Equal(-100f, avg[2], 3);
    }

    [Fact]
    public void Peak_IsPerBinMax()
    {
        var peak = SpectrumProfile.Peak([[-20f, -60f, -100f], [-40f, -20f, -100f]]);
        Assert.Equal(-20f, peak[0]);
        Assert.Equal(-20f, peak[1]);
        Assert.Equal(-100f, peak[2]);
    }

    [Fact]
    public void Empty_YieldsEmpty()
    {
        Assert.Empty(SpectrumProfile.Average([]));
        Assert.Empty(SpectrumProfile.Peak([]));
    }

    [Fact]
    public void Average_NeverExceedsPeak()
    {
        var cols = new[] { new[] { -10f, -80f }, new[] { -50f, -20f }, new[] { -30f, -40f } };
        var avg = SpectrumProfile.Average(cols);
        var peak = SpectrumProfile.Peak(cols);
        for (var k = 0; k < avg.Length; k++) Assert.True(avg[k] <= peak[k] + 1e-3f);
    }
}
