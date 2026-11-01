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

        // Before the palette load and the decode: a refusal to replace a file
        // costs neither, and reaches Main as an OptionException (exit 2).
        var input = parsed.Files[0];
        var outPath = CliOptions.ImageOutPath(input, parsed.OutPath);
        var options = WithPalette(parsed);
        try
        {
            var (w, h, rgb) = new SpectrogramImage(ffmpeg).Render(input, options);
            using var stream = CreateOutput(outPath, replace: parsed.OutPath is not null);
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

    // ImageOutPath saw the default name free, but the decode since took seconds.
    // CreateNew turns a file that appeared meanwhile into the same refusal (exit
    // 2, nothing written) instead of truncating it; an explicit -o keeps
    // create-or-replace. The translation covers the open only: a failure while
    // writing the file just created must not read as "already exists".
    private static FileStream CreateOutput(string path, bool replace)
    {
        try { return File.Open(path, replace ? FileMode.Create : FileMode.CreateNew); }
        catch (IOException) when (!replace && Path.Exists(path)) { throw CliOptions.ImageOutExists(path); }
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
