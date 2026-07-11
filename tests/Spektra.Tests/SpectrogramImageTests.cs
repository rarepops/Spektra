using Spektra.Core;

namespace Spektra.Tests;

public sealed class SpectrogramImageTests
{
    private static (byte R, byte G, byte B) Unpack(uint bgra) =>
        ((byte)(bgra >> 16), (byte)(bgra >> 8), (byte)bgra);

    [Test]
    public async Task ToRgb_PutsBinZeroOnBottomRow()
    {
        const float floor = -120f;
        float[] loudAtDc = [0f, floor, floor];    // bin 0 loud, upper bins at the floor
        float[] quiet = [floor, floor, floor];
        var (w, h, rgb) = SpectrogramImage.ToRgb([loudAtDc, quiet], Colormaps.DefaultLut, floor);

        await Assert.That(w).IsEqualTo(2);
        await Assert.That(h).IsEqualTo(3);
        await Assert.That(rgb.Length).IsEqualTo(2 * 3 * 3);

        // DefaultLut is Turbo; endpoints land exactly on the anchor colors.
        var hot = Unpack(Colormaps.ToBgra(PaletteKind.Turbo, 0f, floor));
        var cold = Unpack(Colormaps.ToBgra(PaletteKind.Turbo, floor, floor));

        var bottomLeft = (h - 1) * w * 3; // column 0, bin 0: the loud cell
        await Assert.That((rgb[bottomLeft], rgb[bottomLeft + 1], rgb[bottomLeft + 2]))
            .IsEqualTo((hot.R, hot.G, hot.B));
        await Assert.That((rgb[0], rgb[1], rgb[2])).IsEqualTo((cold.R, cold.G, cold.B)); // top-left: quiet
    }

    [Test]
    public async Task ToRgb_AppliesTheRequestedPalette()
    {
        const float floor = -120f;
        float[] col = [0f];
        var palettes = PaletteRegistry.LoadWithCustom(Path.Combine(Path.GetTempPath(), "spektra-no-palettes"));
        var (_, _, magma) = SpectrogramImage.ToRgb([col], palettes.BakeLut("Magma", floor), floor);
        var (_, _, gray) = SpectrogramImage.ToRgb([col], palettes.BakeLut("Grayscale", floor), floor);
        await Assert.That(gray.SequenceEqual(magma)).IsFalse();
        await Assert.That((int)gray[0]).IsEqualTo(255); // grayscale at 0 dB is white
        await Assert.That((int)gray[1]).IsEqualTo(255);
        await Assert.That((int)gray[2]).IsEqualTo(255);
    }
}
