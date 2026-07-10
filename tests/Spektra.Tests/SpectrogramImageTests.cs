using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public sealed class SpectrogramImageTests
{
    private static (byte R, byte G, byte B) Unpack(uint bgra) =>
        ((byte)(bgra >> 16), (byte)(bgra >> 8), (byte)bgra);

    [Fact]
    public void ToRgb_PutsBinZeroOnBottomRow()
    {
        const float floor = -120f;
        float[] loudAtDc = [0f, floor, floor];    // bin 0 loud, upper bins at the floor
        float[] quiet = [floor, floor, floor];
        var (w, h, rgb) = SpectrogramImage.ToRgb([loudAtDc, quiet], Colormaps.DefaultLut, floor);

        Assert.Equal(2, w);
        Assert.Equal(3, h);
        Assert.Equal(2 * 3 * 3, rgb.Length);

        var hot = Unpack(Colormaps.ToBgra(PaletteKind.Magma, 0f, floor));
        var cold = Unpack(Colormaps.ToBgra(PaletteKind.Magma, floor, floor));

        var bottomLeft = (h - 1) * w * 3; // column 0, bin 0: the loud cell
        Assert.Equal((hot.R, hot.G, hot.B),
                     (rgb[bottomLeft], rgb[bottomLeft + 1], rgb[bottomLeft + 2]));
        Assert.Equal((cold.R, cold.G, cold.B), (rgb[0], rgb[1], rgb[2])); // top-left: quiet
    }

    [Fact]
    public void ToRgb_AppliesTheRequestedPalette()
    {
        const float floor = -120f;
        float[] col = [0f];
        var palettes = PaletteRegistry.LoadWithCustom(Path.Combine(Path.GetTempPath(), "spektra-no-palettes"));
        var (_, _, magma) = SpectrogramImage.ToRgb([col], palettes.BakeLut("Magma", floor), floor);
        var (_, _, gray) = SpectrogramImage.ToRgb([col], palettes.BakeLut("Grayscale", floor), floor);
        Assert.NotEqual(magma, gray);
        Assert.Equal(255, gray[0]); // grayscale at 0 dB is white
        Assert.Equal(255, gray[1]);
        Assert.Equal(255, gray[2]);
    }
}
