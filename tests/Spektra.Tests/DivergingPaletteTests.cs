using Spektra.Core;

namespace Spektra.Tests;

public class DivergingPaletteTests
{
    private static (int r, int g, int b) Unpack(uint bgra) =>
        ((int)((bgra >> 16) & 0xFF), (int)((bgra >> 8) & 0xFF), (int)(bgra & 0xFF));

    [Test]
    public async Task Zero_IsBlack()
    {
        var (r, g, b) = Unpack(DivergingPalette.ToBgra(0f));
        await Assert.That(r).IsBetween(0, 16);
        await Assert.That(g).IsBetween(0, 16);
        await Assert.That(b).IsBetween(0, 16);
    }

    [Test]
    public async Task Positive_IsRed_Negative_IsBlue()
    {
        var (rp, _, bp) = Unpack(DivergingPalette.ToBgra(40f));
        await Assert.That(rp > bp + 80).IsTrue(); // positive should be red-dominant
        var (rn, _, bn) = Unpack(DivergingPalette.ToBgra(-40f));
        await Assert.That(bn > rn + 80).IsTrue(); // negative should be blue-dominant
    }

    [Test]
    public async Task Clamps_And_Opaque()
    {
        await Assert.That(DivergingPalette.ToBgra(5f) >> 24).IsEqualTo(0xFFu);
        await Assert.That(DivergingPalette.ToBgra(999f)).IsEqualTo(DivergingPalette.ToBgra(40f));
        await Assert.That(DivergingPalette.ToBgra(-999f)).IsEqualTo(DivergingPalette.ToBgra(-40f));
    }
}
