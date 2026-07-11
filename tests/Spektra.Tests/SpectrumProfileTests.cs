using Spektra.Core;

namespace Spektra.Tests;

public class SpectrumProfileTests
{
    [Test]
    public async Task Average_IsPerBinMean()
    {
        var avg = SpectrumProfile.Average([[-20f, -60f, -100f], [-40f, -20f, -100f]]);
        await Assert.That(avg[0]).IsCloseTo(-30f, 0.001f);
        await Assert.That(avg[1]).IsCloseTo(-40f, 0.001f);
        await Assert.That(avg[2]).IsCloseTo(-100f, 0.001f);
    }

    [Test]
    public async Task Peak_IsPerBinMax()
    {
        var peak = SpectrumProfile.Peak([[-20f, -60f, -100f], [-40f, -20f, -100f]]);
        await Assert.That(peak[0]).IsEqualTo(-20f);
        await Assert.That(peak[1]).IsEqualTo(-20f);
        await Assert.That(peak[2]).IsEqualTo(-100f);
    }

    [Test]
    public async Task Empty_YieldsEmpty()
    {
        await Assert.That(SpectrumProfile.Average([])).IsEmpty();
        await Assert.That(SpectrumProfile.Peak([])).IsEmpty();
    }

    [Test]
    public async Task Average_NeverExceedsPeak()
    {
        var cols = new[] { new[] { -10f, -80f }, new[] { -50f, -20f }, new[] { -30f, -40f } };
        var avg = SpectrumProfile.Average(cols);
        var peak = SpectrumProfile.Peak(cols);
        for (var k = 0; k < avg.Length; k++) await Assert.That(avg[k] <= peak[k] + 1e-3f).IsTrue();
    }
}
