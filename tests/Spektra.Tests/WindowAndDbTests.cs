using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class WindowAndDbTests
{
    [Fact]
    public void Hann_HasExpectedShape()
    {
        var w = WindowFunction.Hann(2048);
        Assert.Equal(2048, w.Length);
        Assert.Equal(0f, w[0], 3);
        Assert.Equal(1f, w[1024], 2);            // ~peak at center
        Assert.Equal(w[100], w[2047 - 100], 4);  // symmetric
        Assert.All(w, v => Assert.InRange(v, 0f, 1f));
    }

    [Fact]
    public void Db_ConvertsAndClamps()
    {
        Assert.Equal(0f, Db.FromAmplitude(1f), 3);
        Assert.Equal(-6.02f, Db.FromAmplitude(0.5f), 1);
        Assert.Equal(Db.Floor, Db.FromAmplitude(0f), 3);       // silence clamps to floor
        Assert.Equal(Db.Floor, Db.FromAmplitude(1e-9f), 3);    // below floor clamps
        Assert.Equal(0f, Db.FromAmplitude(1.5f), 3);           // above full scale clamps to 0
    }
}
