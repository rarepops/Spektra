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

        string? outPath = null;
        string? paletteName = null;
        double? gamma = null;
        var options = new ImageOptions();
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "-o" or "--out") outPath = CliOptions.Value(a, args, ref i);
            else if (a is "--palette") paletteName = CliOptions.Value(a, args, ref i);
            else if (a is "--gamma")
            {
                var g = CliOptions.Double(a, args, ref i);
                gamma = g > 0 ? g : throw new OptionException($"--gamma must be greater than 0, got '{g}'.");
            }
            else if (a is "--floor") options = options with { FloorDb = CliOptions.Float(a, args, ref i) };
            else if (a is "--fft") options = options with { WindowSize = CliOptions.Int(a, args, ref i, min: 16) };
            else if (a is "--channel") options = options with { Channel = CliOptions.Int(a, args, ref i, min: 1) - 1 };
            else if (a is "--columns") options = options with { MaxColumns = CliOptions.Int(a, args, ref i, min: 1) };
            else if (a.StartsWith('-')) throw new OptionException($"unknown option '{a}'.");
            else files.Add(a);
        }
        if (files.Count != 1 || Directory.Exists(files[0]))
        {
            Console.Error.WriteLine("spektra-cli image: give one audio file (folders are not supported).");
            return 2;
        }

        // The render follows the app's saved theme (palette + tightness);
        // --palette/--gamma override. Resolved after the loop so a later
        // --floor still shapes db-pinned custom palettes correctly.
        var saved = SettingsStore.Load(SettingsStore.DefaultPath);
        var palettes = PaletteRegistry.LoadWithCustom();
        if (paletteName is not null && !palettes.Has(paletteName))
            Console.Error.WriteLine(
                $"spektra-cli image: unknown palette '{paletteName}'; using turbo. " +
                $"Available: {string.Join(", ", palettes.Names)}.");
        options = options with
        {
            PaletteLut = palettes.BakeLut(
                paletteName ?? saved.Palette, options.FloorDb, gamma: gamma ?? saved.PaletteGamma),
        };

        var input = files[0];
        outPath ??= Path.ChangeExtension(input, ".png");
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
}
