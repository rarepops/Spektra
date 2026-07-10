using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class ColormapsTests
{
    private static (int r, int g, int b) Unpack(uint bgra) =>
        ((int)((bgra >> 16) & 0xFF), (int)((bgra >> 8) & 0xFF), (int)(bgra & 0xFF));

    [Fact]
    public void FloorAndCeil_MapToEndsOfTheMap()
    {
        var floor = Colormaps.ToBgra(PaletteKind.Magma, -100f, -100f, 0f);
        var ceil = Colormaps.ToBgra(PaletteKind.Magma, 0f, -100f, 0f);
        var (fr, fg, fb) = Unpack(floor);
        var (cr, cg, cb) = Unpack(ceil);
        Assert.True(fr + fg + fb < 30, "floor should be near black");
        Assert.True(cr + cg + cb > 600, "ceil should be bright");
    }

    [Fact]
    public void LowerFloor_BrightensAGivenDb()
    {
        // A -60 dB signal sits at 50% of a -120..0 range but only 25% of a
        // -80..0 range, so lowering the floor maps it to a brighter color.
        var low = Unpack(Colormaps.ToBgra(PaletteKind.Magma, -60f, -120f, 0f));
        var high = Unpack(Colormaps.ToBgra(PaletteKind.Magma, -60f, -80f, 0f));
        Assert.True(low.r + low.g + low.b > high.r + high.g + high.b);
    }

    [Fact]
    public void Grayscale_IsNeutral()
    {
        var (r, g, b) = Unpack(Colormaps.ToBgra(PaletteKind.Grayscale, -60f, -120f, 0f));
        Assert.Equal(r, g);
        Assert.Equal(g, b);
    }

    [Fact]
    public void Palettes_DifferFromEachOther()
    {
        var magma = Colormaps.ToBgra(PaletteKind.Magma, -40f, -120f, 0f);
        var viridis = Colormaps.ToBgra(PaletteKind.Viridis, -40f, -120f, 0f);
        var inferno = Colormaps.ToBgra(PaletteKind.Inferno, -40f, -120f, 0f);
        Assert.NotEqual(magma, viridis);
        Assert.NotEqual(magma, inferno);
        Assert.NotEqual(viridis, inferno);
    }

    [Fact]
    public void Opaque_AndClamps()
    {
        Assert.Equal(0xFFu, Colormaps.ToBgra(PaletteKind.Viridis, -60f, -120f, 0f) >> 24);
        Assert.Equal(
            Colormaps.ToBgra(PaletteKind.Magma, -120f, -120f, 0f),
            Colormaps.ToBgra(PaletteKind.Magma, -500f, -120f, 0f));
    }

    [Fact]
    public void DefaultLut_IsTurbo_ClampsAndStaysOpaque()
    {
        var lut = Colormaps.DefaultLut;
        Assert.Equal(Colormaps.ToBgra(PaletteKind.Turbo, 0f, Db.Floor, 0f),
            PaletteLut.Sample(lut, 0f, Db.Floor, 0f));
        Assert.Equal(Colormaps.ToBgra(PaletteKind.Turbo, Db.Floor, Db.Floor, 0f),
            PaletteLut.Sample(lut, Db.Floor, Db.Floor, 0f));
        Assert.Equal(PaletteLut.Sample(lut, Db.Floor, Db.Floor, 0f),
            PaletteLut.Sample(lut, -500f, Db.Floor, 0f)); // clamps below the floor
        Assert.Equal(PaletteLut.Sample(lut, 0f, Db.Floor, 0f),
            PaletteLut.Sample(lut, 10f, Db.Floor, 0f));   // clamps above the ceiling
        Assert.Equal(0xFFu, PaletteLut.Sample(lut, -60f, Db.Floor, 0f) >> 24);
    }
}
