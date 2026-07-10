namespace Spektra.Core;

/// Options for a headless spectrogram render; defaults mirror the desktop
/// app (Magma, -120 dB floor, FFT 2048 Hann, mono mixdown, 2048 columns).
public sealed record ImageOptions(
    uint[]? PaletteLut = null,    // baked via PaletteRegistry; null = Magma
    float FloorDb = -120f,
    int WindowSize = 2048,
    int? Channel = null,          // 0-based decode channel; null = mixdown
    int MaxColumns = 2048);

/// Headless spectrogram bitmap: decode + analyze via AnalysisSession, then
/// colormap into an RGB buffer PngWriter can serialize.
public sealed class SpectrogramImage(FfmpegPaths ffmpeg)
{
    /// Analyzes the whole file (the engine's MaxColumns peak-hold merging
    /// keeps any length within the column budget) and returns the bitmap.
    /// Throws AudioDecodeException when no columns come out.
    public (int Width, int Height, byte[] Rgb) Render(
        string path, ImageOptions options, CancellationToken ct = default)
    {
        var session = new AnalysisSession(ffmpeg);
        var meta = session.ReadMetadata(path);
        var settings = new SpectrogramSettings(
            WindowSize: options.WindowSize, MaxColumns: options.MaxColumns);
        var columns = session.AnalyzeColumns(
            path, meta, settings, ct, new DecodeOptions(Channel: options.Channel)).ToList();
        if (columns.Count == 0)
            throw new AudioDecodeException(
                "The file is shorter than one FFT window; nothing to render.");
        return ToRgb(columns, options.PaletteLut ?? Colormaps.DefaultLut, options.FloorDb);
    }

    /// Pure: columns to RGB, bin 0 (DC) on the bottom row. Requires a
    /// non-empty column list (Render throws on zero columns first).
    public static (int Width, int Height, byte[] Rgb) ToRgb(
        IReadOnlyList<float[]> columns, uint[] paletteLut, float floorDb)
    {
        var width = columns.Count;
        var height = columns[0].Length;
        var rgb = new byte[width * height * 3];
        for (var x = 0; x < width; x++)
        {
            var col = columns[x];
            for (var bin = 0; bin < height; bin++)
            {
                var v = PaletteLut.Sample(paletteLut, col[bin], floorDb, 0f);
                var p = ((height - 1 - bin) * width + x) * 3;
                rgb[p] = (byte)(v >> 16);
                rgb[p + 1] = (byte)(v >> 8);
                rgb[p + 2] = (byte)v;
            }
        }
        return (width, height, rgb);
    }
}
