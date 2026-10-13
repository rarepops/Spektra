using Spektra.Core;

namespace Spektra.Cli;

internal static partial class Program
{
    private static int Image(FfmpegPaths ffmpeg, string[] args, OutFormat fmt)
    {
        if (fmt != OutFormat.Text)
        {
            Console.Error.WriteLine("spektra-cli image: --json/--csv do not apply to image.");
            return 2;
        }

        var parsed = CliOptions.ParseImage(args);
        if (parsed.Files.Count != 1 || Directory.Exists(parsed.Files[0]))
        {
            Console.Error.WriteLine("spektra-cli image: give one audio file (folders are not supported).");
            return 2;
        }

        var options = WithPalette(parsed);
        var input = parsed.Files[0];
        var outPath = parsed.OutPath ?? Path.ChangeExtension(input, ".png");
        try
        {
            var (w, h, rgb) = new SpectrogramImage(ffmpeg).Render(input, options);
            using var stream = File.Create(outPath);
            PngWriter.Write(stream, w, h, rgb);
            Console.WriteLine($"Wrote {outPath} ({w}x{h})");
            return 0;
        }
        catch (Exception ex) when (
            ex is AudioDecodeException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"spektra-cli image: {ex.Message}");
            return 2;
        }
    }

    // The render follows the app's saved theme (palette + tightness);
    // --palette/--gamma override. Resolved after the flags are read so a later
    // --floor still shapes db-pinned custom palettes correctly.
    private static ImageOptions WithPalette(ImageArgs parsed)
    {
        var saved = SettingsStore.Load(SettingsStore.DefaultPath);
        var palettes = PaletteRegistry.LoadWithCustom();
        if (parsed.PaletteName is not null && !palettes.Has(parsed.PaletteName))
            Console.Error.WriteLine(
                $"spektra-cli image: unknown palette '{parsed.PaletteName}'; using turbo. " +
                $"Available: {string.Join(", ", palettes.Names)}.");
        return parsed.Options with
        {
            PaletteLut = palettes.BakeLut(
                parsed.PaletteName ?? saved.Palette, parsed.Options.FloorDb,
                gamma: parsed.Gamma ?? saved.PaletteGamma),
        };
    }
}
