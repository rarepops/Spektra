namespace Spektra.Core;

/// Options for a headless spectrogram render; defaults mirror the desktop
/// app (Magma, -120 dB floor, FFT 2048 Hann, mono mixdown, 2048 columns).
public sealed record ImageOptions(
    PaletteKind Palette = PaletteKind.Magma,
    float FloorDb = -120f,
    int WindowSize = 2048,
    int? Channel = null,          // 0-based decode channel; null = mixdown
    int MaxColumns = 2048);

/// Headless spectrogram bitmap: decode + analyze via AnalysisSession, then
/// colormap into an RGB buffer PngWriter can serialize.
public sealed class SpectrogramImage(FfmpegPaths ffmpeg)
{
    /// Pure: columns to RGB, bin 0 (DC) on the bottom row. Requires a
    /// non-empty column list (Render throws on zero columns first).
    public static (int Width, int Height, byte[] Rgb) ToRgb(
        IReadOnlyList<float[]> columns, PaletteKind palette, float floorDb)
    {
        var width = columns.Count;
        var height = columns[0].Length;
        var rgb = new byte[width * height * 3];
        for (var x = 0; x < width; x++)
        {
            var col = columns[x];
            for (var bin = 0; bin < height; bin++)
            {
                var v = Colormaps.ToBgra(palette, col[bin], floorDb);
                var p = ((height - 1 - bin) * width + x) * 3;
                rgb[p] = (byte)(v >> 16);
                rgb[p + 1] = (byte)(v >> 8);
                rgb[p + 2] = (byte)v;
            }
        }
        return (width, height, rgb);
    }
}
