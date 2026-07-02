using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class PaletteTests
{
    private static (int r, int g, int b) Unpack(uint bgra) =>
        ((int)((bgra >> 16) & 0xFF), (int)((bgra >> 8) & 0xFF), (int)(bgra & 0xFF));

    [Fact]
    public void FloorIsNearBlack_ZeroDbIsBright()
    {
        var (r0, g0, b0) = Unpack(Palette.ToBgra(Db.Floor));
        Assert.True(r0 + g0 + b0 < 30, "floor should be near black");

        var (r1, g1, b1) = Unpack(Palette.ToBgra(0f));
        Assert.True(r1 + g1 + b1 > 600, "0 dB should be near white/yellow");
    }

    [Fact]
    public void BrightnessIncreasesMonotonically()
    {
        var prev = -1;
        foreach (var db in new[] { -120f, -90f, -60f, -30f, 0f })
        {
            var (r, g, b) = Unpack(Palette.ToBgra(db));
            var sum = r + g + b;
            Assert.True(sum > prev, $"brightness must rise ({db} dB: {sum} <= {prev})");
            prev = sum;
        }
    }

    [Fact]
    public void AlphaIsOpaque_AndOutOfRangeClamps()
    {
        Assert.Equal(0xFFu, Palette.ToBgra(-60f) >> 24);
        Assert.Equal(Palette.ToBgra(Db.Floor), Palette.ToBgra(-500f));
        Assert.Equal(Palette.ToBgra(0f), Palette.ToBgra(10f));
    }
}
