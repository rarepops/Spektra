using System.Reflection;
using Spektra.Core;

namespace Spektra.Cli;

/// Cross-platform command-line front end for Spektra's analysis engine. Writes
/// to stdout like a normal console program, so output pipes and redirects
/// cleanly. Exit code 1 when anything is judged likely lossy, 2 on setup errors.
internal static partial class Program
{
    public static int Main(string[] args)
    {
        // UTF-8 output regardless of the console's legacy codepage: summaries
        // and compare lines carry middots and Δ, which the OEM codepage
        // mangles in redirects and pipes.
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
        catch (IOException) { /* no console to configure */ }

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            return Usage(args.Length == 0 ? 2 : 0);

        if (args[0] is "--version" or "-v")
        {
            Console.WriteLine($"spektra-cli {Version()}");
            return 0;
        }

        // The manifest never decodes (it lists and reads cache), so it is the
        // one verb that must keep working without ffmpeg installed; it
        // dispatches before the gate.
        if (args[0] is "--manifest" or "manifest")
        {
            try
            {
                var (fmt, _, rest) = CliOptions.Take(args[1..], DefaultJobs);
                return Manifest(rest, fmt);
            }
            catch (OptionException ex)
            {
                Console.Error.WriteLine($"spektra-cli: {ex.Message}");
                return 2;
            }
        }

        var ffmpeg = FfmpegLocator.LocateDefault();
        if (ffmpeg is null)
        {
            Console.Error.WriteLine(
                "spektra-cli: ffmpeg/ffprobe not found. Install ffmpeg and ensure it is on PATH.");
            return 2;
        }

        try
        {
            var (fmt, jobs, rest) = CliOptions.Take(args[1..], DefaultJobs);
            return args[0] switch
            {
                "--report" or "report" => Report(ffmpeg, rest, fmt, jobs),
                "--scan" or "scan" => Scan(ffmpeg, rest, fmt, jobs),
                "--check" or "check" => Check(ffmpeg, rest, fmt, jobs),
                "--audit" or "audit" => Audit(ffmpeg, rest, fmt, jobs),
                "--dupes" or "dupes" => Dupes(ffmpeg, rest, fmt, jobs),
                "--inventory" or "inventory" => Inventory(ffmpeg, rest, fmt, jobs),
                "--loudness" or "loudness" => Loudness(ffmpeg, rest, fmt, jobs),
                "--diff" or "diff" => Diff(ffmpeg, rest, fmt),
                "--image" or "image" => Image(ffmpeg, rest, fmt),
                _ => Usage(2),
            };
        }
        catch (OptionException ex)
        {
            Console.Error.WriteLine($"spektra-cli: {ex.Message}");
            return 2;
        }
    }

    // Default worker count: about 80% of the logical cores, leaving headroom for
    // the OS and for ffmpeg's own decode threads. Override with --jobs / -j.
    private static readonly int DefaultJobs = Math.Max(1, (int)(Environment.ProcessorCount * 0.8));

    // Informational version (the csproj <Version>), minus any +buildmetadata.
    private static string Version()
    {
        var asm = typeof(Program).Assembly;
        var v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion
                ?? asm.GetName().Version?.ToString() ?? "unknown";
        var plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }

    // Analyzes files concurrently, capped at `jobs` workers, and returns results
    // in input order. Each file spawns its own ffmpeg/ffprobe and runs its own
    // FFT, so the work is CPU-bound and safe to run in parallel; ffmpeg streams
    // each file from disk itself, which is why we parallelize whole files rather
    // than prefetching bytes into a shared queue.
    private static T[] MapParallel<T>(IReadOnlyList<string> files, int jobs, Func<string, T> analyze)
    {
        var results = new T[files.Count];
        Parallel.For(0, files.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs) },
            i => results[i] = analyze(files[i]));
        return results;
    }

    // A single folder argument recurses into its audio files; otherwise the
    // arguments are taken as individual files.
    private static IReadOnlyList<string> ResolveInputs(string[] paths)
    {
        CliOptions.RejectUnknownFlags(paths);
        return paths.Length == 1 && Directory.Exists(paths[0])
            ? BandwidthReport.FindAudioFiles(paths[0]).ToList()
            : paths;
    }

    private static void Emit<T>(IReadOnlyList<T> rows, OutFormat fmt)
    {
        if (fmt == OutFormat.Json) Console.WriteLine(Reporting.ToJson(rows));
        else Console.Write(Reporting.ToCsv(rows));
    }

    private static int Usage(int exitCode)
    {
        Console.WriteLine("""
            Spektra - audio bandwidth / integrity analyzer

            Usage:
              spektra-cli report <file|folder> ...   Bandwidth verdict per file.
              spektra-cli scan <folder>              Compact bandwidth scan of a library.
              spektra-cli check <file|folder> ...    Integrity check (corruption / missing data).
              spektra-cli audit <file|folder> ...    Bandwidth + integrity together (cached).
              spektra-cli dupes <folder> ...         Find duplicate songs; mark the best copy (cached).
              spektra-cli manifest <folder>          List EVERYTHING in a folder with type chips (no decoding).
              spektra-cli inventory <folder>         Tags + embedded art per file, machine-readable (no decoding).
              spektra-cli loudness <file|folder> ... Loudness (LUFS), true peak, and dynamics.
              spektra-cli diff <fileA> <fileB>       Compare two files: align, spectral diff, null test.
              spektra-cli image <file>               Render the spectrogram to a PNG (no window).
              spektra-cli --version                  Print the version.
              spektra-cli --help                     Show this help.

            Add --json or --csv to any command for a machine-readable report,
            e.g. spektra-cli scan Music --csv > report.csv

            Folders are analyzed in parallel. By default Spektra uses about 80% of
            the CPU cores; cap it with --jobs N (or -j N), e.g. spektra-cli scan Music -j 4

            audit caches results per file (keyed by size + mtime) in the app data
            folder, so repeat runs only analyze new or changed files; --fresh re-analyzes.
            Add --html out.html to also write the results as a self-contained HTML page.

            dupes finds the same recording across folders and formats (audio
            fingerprint match, not filename), rides the same cache as audit, and
            marks each group's best copy; give two or more folders to also find
            duplicates that live in different libraries. --fresh re-analyzes.
            Add --html out.html to also write the groups as a self-contained HTML page.

            manifest lists every file, audio or not, with a type chip per file;
            files the audit has seen before get their real codec and verdict
            chips from the cache. It never decodes, works without ffmpeg, and
            --html out.html writes the collapsible tree page.

            diff aligns the files automatically; pin an offset with --offset <ms>
            (positive = B later than A) and tune the verdict with --threshold-db <N>
            (default 40: the null depth needed to count as SAME).

            image options: -o <out.png>, --palette <name>, --gamma <g> (the app's
            Tightness), --floor <dB>, --fft <size>, --channel <n>, --columns <max
            width>. Palette and gamma default to your app settings; the rest to
            -120, 2048, mix, 2048. --palette also accepts custom palettes: JSON
            files dropped in %APPDATA%\Spektra\palettes or a palettes folder next
            to the app (see docs/cli.md for the format).

            Exit code is 1 on findings (report/scan: lossy, upsampled, or mixed; check:
            corrupt files; audit: a transcode, an upsample, or corruption - an
            honest lossy file is not a problem; dupes: one or more duplicate
            groups found; loudness: a file could not be measured; diff: the
            files differ), 2 on setup errors (an unknown or malformed
            option, or no existing file or folder given).
            Requires ffmpeg + ffprobe on PATH.
            """);
        return exitCode;
    }
}
