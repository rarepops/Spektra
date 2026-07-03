using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class TimeMapTests
{
    [Fact]
    public void Default_ReproducesFileDurationMapping()
    {
        var m = new TimeMap(10);
        Assert.Equal(5, m.StartSeconds(0.5), 6);
        Assert.Equal(2, m.DurationSeconds(0.2), 6);
    }

    [Fact]
    public void Shift_DelaysStart_ButNotDuration()
    {
        var m = new TimeMap(10, 0.25);
        Assert.Equal(5.25, m.StartSeconds(0.5), 6); // t0*ref + shift
        Assert.Equal(2, m.DurationSeconds(0.2), 6);
    }
}
