using Spektra.Core;

namespace Spektra.Tests;

public class TimeMapTests
{
    [Test]
    public async Task Default_ReproducesFileDurationMapping()
    {
        var m = new TimeMap(10);
        await Assert.That(m.StartSeconds(0.5)).IsCloseTo(5, 1e-6);
        await Assert.That(m.DurationSeconds(0.2)).IsCloseTo(2, 1e-6);
    }

    [Test]
    public async Task Shift_DelaysStart_ButNotDuration()
    {
        var m = new TimeMap(10, 0.25);
        await Assert.That(m.StartSeconds(0.5)).IsCloseTo(5.25, 1e-6); // t0*ref + shift
        await Assert.That(m.DurationSeconds(0.2)).IsCloseTo(2, 1e-6);
    }
}
