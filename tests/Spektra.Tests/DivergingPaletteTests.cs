using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class DivergingPaletteTests
{
    private static (int r, int g, int b) Unpack(uint bgra) =>
        ((int)((bgra >> 16) & 0xFF), (int)((bgra >> 8) & 0xFF), (int)(bgra & 0xFF));

    [Fact]
    public void Zero_IsBlack()
    {
        var (r, g, b) = Unpack(DivergingPalette.ToBgra(0f));
        Assert.InRange(r, 0, 16);
        Assert.InRange(g, 0, 16);
        Assert.InRange(b, 0, 16);
    }

    [Fact]
    public void Positive_IsRed_Negative_IsBlue()
    {
        var (rp, _, bp) = Unpack(DivergingPalette.ToBgra(40f));
        Assert.True(rp > bp + 80, $"positive should be red-dominant (r={rp}, b={bp})");
        var (rn, _, bn) = Unpack(DivergingPalette.ToBgra(-40f));
        Assert.True(bn > rn + 80, $"negative should be blue-dominant (r={rn}, b={bn})");
    }

    [Fact]
    public void Clamps_And_Opaque()
    {
        Assert.Equal(0xFFu, DivergingPalette.ToBgra(5f) >> 24);
        Assert.Equal(DivergingPalette.ToBgra(40f), DivergingPalette.ToBgra(999f));
        Assert.Equal(DivergingPalette.ToBgra(-40f), DivergingPalette.ToBgra(-999f));
    }
}
