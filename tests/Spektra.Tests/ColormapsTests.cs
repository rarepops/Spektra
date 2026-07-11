using Spektra.Core;

namespace Spektra.Tests;

public class ColormapsTests
{
    private static (int r, int g, int b) Unpack(uint bgra) =>
        ((int)((bgra >> 16) & 0xFF), (int)((bgra >> 8) & 0xFF), (int)(bgra & 0xFF));

    [Test]
    public async Task FloorAndCeil_MapToEndsOfTheMap()
    {
        var floor = Colormaps.ToBgra(PaletteKind.Magma, -100f, -100f, 0f);
        var ceil = Colormaps.ToBgra(PaletteKind.Magma, 0f, -100f, 0f);
        var (fr, fg, fb) = Unpack(floor);
        var (cr, cg, cb) = Unpack(ceil);
        await Assert.That(fr + fg + fb < 30).IsTrue();  // floor should be near black
        await Assert.That(cr + cg + cb > 600).IsTrue(); // ceil should be bright
    }

    [Test]
    public async Task LowerFloor_BrightensAGivenDb()
    {
        // A -60 dB signal sits at 50% of a -120..0 range but only 25% of a
        // -80..0 range, so lowering the floor maps it to a brighter color.
        var low = Unpack(Colormaps.ToBgra(PaletteKind.Magma, -60f, -120f, 0f));
        var high = Unpack(Colormaps.ToBgra(PaletteKind.Magma, -60f, -80f, 0f));
        await Assert.That(low.r + low.g + low.b > high.r + high.g + high.b).IsTrue();
    }

    [Test]
    public async Task Grayscale_IsNeutral()
    {
        var (r, g, b) = Unpack(Colormaps.ToBgra(PaletteKind.Grayscale, -60f, -120f, 0f));
        await Assert.That(g).IsEqualTo(r);
        await Assert.That(b).IsEqualTo(g);
    }

    [Test]
    public async Task Palettes_DifferFromEachOther()
    {
        var magma = Colormaps.ToBgra(PaletteKind.Magma, -40f, -120f, 0f);
        var viridis = Colormaps.ToBgra(PaletteKind.Viridis, -40f, -120f, 0f);
        var inferno = Colormaps.ToBgra(PaletteKind.Inferno, -40f, -120f, 0f);
        await Assert.That(viridis).IsNotEqualTo(magma);
        await Assert.That(inferno).IsNotEqualTo(magma);
        await Assert.That(inferno).IsNotEqualTo(viridis);
    }

    [Test]
    public async Task Opaque_AndClamps()
    {
        await Assert.That(Colormaps.ToBgra(PaletteKind.Viridis, -60f, -120f, 0f) >> 24).IsEqualTo(0xFFu);
        await Assert.That(Colormaps.ToBgra(PaletteKind.Magma, -500f, -120f, 0f))
            .IsEqualTo(Colormaps.ToBgra(PaletteKind.Magma, -120f, -120f, 0f));
    }

    [Test]
    public async Task DefaultLut_IsTurbo_ClampsAndStaysOpaque()
    {
        var lut = Colormaps.DefaultLut;
        await Assert.That(PaletteLut.Sample(lut, 0f, Db.Floor, 0f))
            .IsEqualTo(Colormaps.ToBgra(PaletteKind.Turbo, 0f, Db.Floor, 0f));
        await Assert.That(PaletteLut.Sample(lut, Db.Floor, Db.Floor, 0f))
            .IsEqualTo(Colormaps.ToBgra(PaletteKind.Turbo, Db.Floor, Db.Floor, 0f));
        await Assert.That(PaletteLut.Sample(lut, -500f, Db.Floor, 0f))
            .IsEqualTo(PaletteLut.Sample(lut, Db.Floor, Db.Floor, 0f)); // clamps below the floor
        await Assert.That(PaletteLut.Sample(lut, 10f, Db.Floor, 0f))
            .IsEqualTo(PaletteLut.Sample(lut, 0f, Db.Floor, 0f));       // clamps above the ceiling
        await Assert.That(PaletteLut.Sample(lut, -60f, Db.Floor, 0f) >> 24).IsEqualTo(0xFFu);
    }
}
