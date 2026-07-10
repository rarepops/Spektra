using System.Globalization;
using System.Reflection;
using Spektra.Core;
using CompareOptions = Spektra.Core.CompareOptions;

namespace Spektra.Cli;

/// Cross-platform command-line front end for Spektra's analysis engine. Writes
/// to stdout like a normal console program, so output pipes and redirects
/// cleanly. Exit code 1 when anything is judged likely lossy, 2 on setup errors.
internal static class Program
{
    private enum OutFormat { Text, Json, Csv }

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            return Usage(args.Length == 0 ? 1 : 0);

        if (args[0] is "--version" or "-v")
        {
            Console.WriteLine($"spektra {Version()}");
            return 0;
        }

        var ffmpeg = FfmpegLocator.LocateDefault();
        if (ffmpeg is null)
        {
            Console.Error.WriteLine(
                "spektra: ffmpeg/ffprobe not found. Install ffmpeg and ensure it is on PATH.");
            return 2;
        }

        var (fmt, jobs, rest) = TakeOptions(args[1..]);
        return args[0] switch
        {
            "--report" or "report" => Report(ffmpeg, rest, fmt, jobs),
            "--scan" or "scan" => Scan(ffmpeg, rest, fmt, jobs),
            "--check" or "check" => Check(ffmpeg, rest, fmt, jobs),
            "--audit" or "audit" => Audit(ffmpeg, rest, fmt, jobs),
            "--loudness" or "loudness" => Loudness(ffmpeg, rest, fmt, jobs),
            "--diff" or "diff" => Diff(ffmpeg, rest, fmt),
            "--image" or "image" => Image(ffmpeg, rest, fmt),
            _ => Usage(1),
        };
    }

    // Default worker count: about 80% of the logical cores, leaving headroom for
    // the OS and for ffmpeg's own decode threads. Override with --jobs / -j.
    private static readonly int DefaultJobs = Math.Max(1, (int)(Environment.ProcessorCount * 0.8));

    private static (OutFormat fmt, int jobs, string[] rest) TakeOptions(string[] args)
    {
        var fmt = OutFormat.Text;
        var jobs = DefaultJobs;
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--json") fmt = OutFormat.Json;
            else if (a is "--csv") fmt = OutFormat.Csv;
            else if ((a is "--jobs" or "-j") && i + 1 < args.Length
                     && int.TryParse(args[i + 1], out var j) && j > 0)
            {
                jobs = j;
                i++;
            }
            else rest.Add(a);
        }
        return (fmt, jobs, rest.ToArray());
    }

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
    private static IReadOnlyList<string> ResolveInputs(string[] paths) =>
        paths.Length == 1 && Directory.Exists(paths[0])
            ? BandwidthReport.FindAudioFiles(paths[0]).ToList()
            : paths;

    private static void Emit<T>(IReadOnlyList<T> rows, OutFormat fmt)
    {
        if (fmt == OutFormat.Json) Console.WriteLine(Reporting.ToJson(rows));
        else Console.Write(Reporting.ToCsv(rows));
    }

    private static int Report(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt, int jobs)
    {
        var files = ResolveInputs(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("spektra report: give one or more audio files or a folder.");
            return Usage(1);
        }
        var reports = MapParallel(files, jobs, f => BandwidthReport.Analyze(ffmpeg, f));

        if (fmt != OutFormat.Text)
            Emit(reports.Select(Reporting.ToBandwidthRow).ToList(), fmt);
        else
            foreach (var r in reports)
            {
                Console.WriteLine(Path.GetFileName(r.Path));
                if (r.Error is not null) { Console.WriteLine($"  error: {r.Error}"); continue; }
                Console.WriteLine("  " + r.Metadata!.ToDisplayLine(Path.GetFileName(r.Path)));
                Console.WriteLine("  " + r.Verdict!.Summary);
            }
        return reports.Any(r => r.Verdict?.Kind is VerdictKind.Lossy or VerdictKind.Upsampled) ? 1 : 0;
    }

    private static int Scan(FfmpegPaths ffmpeg, string[] args, OutFormat fmt, int jobs)
    {
        if (args.Length == 0 || !Directory.Exists(args[0]))
        {
            Console.Error.WriteLine("spektra scan: give an existing folder to scan.");
            return Usage(1);
        }
        var root = args[0];
        var files = BandwidthReport.FindAudioFiles(root).ToList();
        var reports = MapParallel(files, jobs, f => BandwidthReport.Analyze(ffmpeg, f));

        if (fmt != OutFormat.Text)
        {
            Emit(reports.Select(Reporting.ToBandwidthRow).ToList(), fmt);
            return reports.Any(r => r.Verdict?.Kind is VerdictKind.Lossy or VerdictKind.Upsampled) ? 1 : 0;
        }

        Console.WriteLine($"Scanning {files.Count} audio file(s) under {root} ...");
        int lossless = 0, suspect = 0, lossy = 0, upsampled = 0, unknown = 0, errors = 0;
        foreach (var r in reports)
        {
            Console.WriteLine($"  {Tag(r)}  {Path.GetRelativePath(root, r.Path)}");
            switch (r.Verdict?.Kind)
            {
                case VerdictKind.Lossless: lossless++; break;
                case VerdictKind.Suspicious: suspect++; break;
                case VerdictKind.Lossy: lossy++; break;
                case VerdictKind.Upsampled: upsampled++; break;
                default: if (r.Error is not null) errors++; else unknown++; break;
            }
        }
        Console.WriteLine(
            $"{Environment.NewLine}{files.Count} files: {lossless} lossless, {suspect} suspect, " +
            $"{lossy} likely lossy, {upsampled} upsampled, {unknown} unknown, {errors} errors.");
        return lossy > 0 || upsampled > 0 ? 1 : 0;
    }

    private static int Check(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt, int jobs)
    {
        var files = ResolveInputs(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("spektra check: give one or more audio files or a folder.");
            return Usage(1);
        }
        var computed = MapParallel(files, jobs, path =>
        {
            try
            {
                var meta = new AnalysisSession(ffmpeg).ReadMetadata(path);
                return (ir: (IntegrityReport?)new IntegrityScanner(ffmpeg).Check(path, meta), err: (string?)null);
            }
            catch (Exception ex) when (ex is AudioDecodeException or IOException)
            {
                return (ir: (IntegrityReport?)null, err: (string?)ex.Message);
            }
        });

        var rows = new List<IntegrityRow>();
        int ok = 0, suspect = 0, corrupt = 0;
        for (var i = 0; i < files.Count; i++)
        {
            var path = files[i];
            var (ir, err) = computed[i];
            rows.Add(Reporting.ToIntegrityRow(path, ir, err));

            if (err is not null || ir?.Status == IntegrityStatus.Corrupt) corrupt++;
            else if (ir?.Status == IntegrityStatus.Suspect) suspect++;
            else ok++;

            if (fmt == OutFormat.Text)
            {
                if (ir?.Status == IntegrityStatus.Ok)
                    Console.WriteLine($"  [OK]      {Path.GetFileName(path)}");
                else
                {
                    var tag = err is not null ? "CORRUPT" : ir!.Status.ToString().ToUpperInvariant();
                    Console.WriteLine($"  [{tag}] {Path.GetFileName(path)} - {err ?? ir!.Summary}");
                }
            }
        }
        if (fmt != OutFormat.Text) Emit(rows, fmt);
        else Console.WriteLine(
            $"{Environment.NewLine}{files.Count} files: {ok} ok, {suspect} suspect, {corrupt} corrupt.");
        return corrupt > 0 ? 1 : 0;
    }

    private static int Audit(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt, int jobs)
    {
        var fresh = paths.Contains("--fresh");
        paths = paths.Where(p => p != "--fresh").ToArray();
        var folder = paths.Length == 1 && Directory.Exists(paths[0]) ? paths[0] : null;
        var targets = folder is not null
            ? FolderAudit.CollectTargets(folder)
            : paths.Select(p =>
            {
                var info = new FileInfo(p);
                return new AuditTarget(p, info.Exists ? info.Length : 0, info.Exists ? info.LastWriteTimeUtc.Ticks : 0);
            }).ToArray();
        if (targets.Length == 0)
        {
            Console.Error.WriteLine("spektra audit: give one or more audio files or a folder.");
            return Usage(1);
        }

        AuditCache? cache = null;
        try { cache = AuditCache.Open(AuditCache.DefaultPath); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"spektra audit: cache unavailable ({ex.Message}); analyzing everything.");
        }

        AuditEntry[] results;
        try
        {
            results = FolderAudit.Run(ffmpeg, targets, jobs, cache, fresh);
            if (folder is not null)
                cache?.PruneFolder(folder, targets.Select(t => t.Path).ToList());
        }
        finally { cache?.Dispose(); }

        if (fmt != OutFormat.Text)
        {
            Emit(results.Select(r => r.Row).ToList(), fmt);
            return results.Any(r => r.HasProblem) ? 1 : 0;
        }

        foreach (var r in results)
        {
            var row = r.Row;
            var bw = row.Error is not null ? "error"
                : $"{row.Bandwidth}{(row.CutoffHz is { } hz ? $" {hz / 1000:0.0}k" : "")}";
            var integ = row.Error is not null ? "ERROR" : row.Integrity;
            Console.WriteLine($"  bandwidth={bw,-18} integrity={integ,-8} {row.File}");
        }
        Console.WriteLine($"{Environment.NewLine}{targets.Length} files, {results.Count(r => r.HasProblem)} with problems.");
        return results.Any(r => r.HasProblem) ? 1 : 0;
    }

    private static int Loudness(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt, int jobs)
    {
        var files = ResolveInputs(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("spektra loudness: give one or more audio files or a folder.");
            return Usage(1);
        }
        var computed = MapParallel(files, jobs, path =>
        {
            try { return (r: (LoudnessReport?)new LoudnessMeasurer(ffmpeg).Measure(path), err: (string?)null); }
            catch (Exception ex) when (ex is AudioDecodeException or IOException) { return (r: (LoudnessReport?)null, err: (string?)ex.Message); }
        });

        var rows = new List<LoudnessRow>();
        for (var i = 0; i < files.Count; i++)
        {
            var path = files[i];
            var (r, err) = computed[i];
            rows.Add(Reporting.ToLoudnessRow(path, r, err));
            if (fmt == OutFormat.Text)
            {
                Console.WriteLine(Path.GetFileName(path));
                Console.WriteLine("  " + (err ?? r!.Summary));
            }
        }
        if (fmt != OutFormat.Text) Emit(rows, fmt);
        return 0;
    }

    private static int Diff(FfmpegPaths ffmpeg, string[] args, OutFormat fmt)
    {
        double? offsetMs = null;
        var thresholdDb = 40f;
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--offset" && i + 1 < args.Length && double.TryParse(
                    args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
            {
                offsetMs = ms;
                i++;
            }
            else if (a is "--threshold-db" && i + 1 < args.Length && float.TryParse(
                    args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
            {
                thresholdDb = t;
                i++;
            }
            else files.Add(a);
        }
        if (files.Count != 2)
        {
            Console.Error.WriteLine("spektra diff: give exactly two audio files.");
            return Usage(1);
        }

        var options = new CompareOptions(OffsetSeconds: offsetMs / 1000, ThresholdDb: thresholdDb);
        CompareReport report;
        try { report = new AudioCompare(ffmpeg).Run(files[0], files[1], options); }
        catch (Exception ex) when (ex is AudioDecodeException or IOException)
        {
            Console.Error.WriteLine($"spektra diff: {ex.Message}");
            return 2;
        }

        if (fmt != OutFormat.Text)
        {
            Emit(new[] { Reporting.ToCompareRow(report) }, fmt);
            return report.IsSame ? 0 : 1;
        }

        Console.WriteLine("A: " + report.MetaA.ToDisplayLine(Path.GetFileName(report.PathA)));
        Console.WriteLine("B: " + report.MetaB.ToDisplayLine(Path.GetFileName(report.PathB)));
        var overlap = AudioMetadata.FormatDuration(TimeSpan.FromSeconds(report.OverlapSeconds));
        var offsetText = $"{report.OffsetSeconds * 1000:+0.###;-0.###} ms";
        Console.WriteLine(report.AlignConfidence is { } conf
            ? $"Aligned {offsetText} (confidence {conf:0.00}) · overlap {overlap}"
            : $"Offset {offsetText} (pinned) · overlap {overlap}");
        if (report.LowConfidence)
            Console.WriteLine(
                $"warning: alignment confidence {report.AlignConfidence:0.00} is low; the offset may be wrong");
        if (report.HasDrift)
            Console.WriteLine(
                $"note: alignment drifts about {report.DriftSeconds * 1000:0} ms across the file; " +
                "a single offset can't fully align it");
        Console.WriteLine("Spectral: " + report.Diff.Summary);
        Console.WriteLine("Null:     " + report.Null.Summary);
        Console.WriteLine(VerdictLine(report));
        return report.IsSame ? 0 : 1;
    }

    private static string VerdictLine(CompareReport r)
    {
        if (r.Null.ResidualRmsDb <= Db.Floor + 1f)
            return "SAME      perfect null (identical samples)";
        return r.IsSame
            ? $"SAME      null depth {r.Null.NullDepthDb:0.0} dB >= threshold {r.ThresholdDb:0.0} dB"
            : $"DIFFERS   null depth {r.Null.NullDepthDb:0.0} dB < threshold {r.ThresholdDb:0.0} dB";
    }

    private static int Image(FfmpegPaths ffmpeg, string[] args, OutFormat fmt)
    {
        if (fmt != OutFormat.Text)
        {
            Console.Error.WriteLine("spektra image: --json/--csv do not apply to image.");
            return Usage(1);
        }

        string? outPath = null;
        string? paletteName = null;
        double? gamma = null;
        var options = new ImageOptions();
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            var value = i + 1 < args.Length ? args[i + 1] : null;
            if (a is "-o" or "--out" && value is not null) { outPath = value; i++; }
            else if (a is "--palette" && value is not null) { paletteName = value; i++; }
            else if (a is "--gamma" && value is not null && double.TryParse(
                         value, NumberStyles.Float, CultureInfo.InvariantCulture, out var g) && g > 0)
            { gamma = g; i++; }
            else if (a is "--floor" && value is not null && float.TryParse(
                         value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floor))
            { options = options with { FloorDb = floor }; i++; }
            else if (a is "--fft" && value is not null
                     && int.TryParse(value, out var fft) && fft >= 16)
            { options = options with { WindowSize = fft }; i++; }
            else if (a is "--channel" && value is not null
                     && int.TryParse(value, out var ch) && ch >= 1)
            { options = options with { Channel = ch - 1 }; i++; }
            else if (a is "--columns" && value is not null
                     && int.TryParse(value, out var cols) && cols >= 1)
            { options = options with { MaxColumns = cols }; i++; }
            else files.Add(a);
        }
        if (files.Count != 1 || Directory.Exists(files[0]))
        {
            Console.Error.WriteLine("spektra image: give one audio file (folders are not supported).");
            return Usage(1);
        }

        // The render follows the app's saved theme (palette + tightness);
        // --palette/--gamma override. Resolved after the loop so a later
        // --floor still shapes db-pinned custom palettes correctly.
        var saved = SettingsStore.Load(SettingsStore.DefaultPath);
        var palettes = PaletteRegistry.LoadWithCustom();
        if (paletteName is not null && !palettes.Has(paletteName))
            Console.Error.WriteLine(
                $"spektra image: unknown palette '{paletteName}'; using turbo. " +
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
            Console.Error.WriteLine($"spektra image: {ex.Message}");
            return 2;
        }
    }

    private static string Tag(FileReport r)
    {
        if (r.Error is not null) return "[ERROR   ]      ";
        var kind = r.Verdict!.Kind switch
        {
            VerdictKind.Lossless => "[LOSSLESS]",
            VerdictKind.Suspicious => "[SUSPECT ]",
            VerdictKind.Lossy => "[LOSSY   ]",
            VerdictKind.Upsampled => "[UPSAMPLE]",
            _ => "[UNKNOWN ]",
        };
        var cut = r.Verdict.CutoffHz is { } hz ? $" {hz / 1000:0.0}k".PadRight(6) : "      ";
        return kind + cut;
    }

    private static int Usage(int exitCode)
    {
        Console.WriteLine("""
            Spektra - audio bandwidth / integrity analyzer

            Usage:
              spektra report <file|folder> ...   Bandwidth verdict per file.
              spektra scan <folder>              Compact bandwidth scan of a library.
              spektra check <file|folder> ...    Integrity check (corruption / missing data).
              spektra audit <file|folder> ...    Bandwidth + integrity together (cached).
              spektra loudness <file|folder> ... Loudness (LUFS), true peak, and dynamics.
              spektra diff <fileA> <fileB>       Compare two files: align, spectral diff, null test.
              spektra image <file>               Render the spectrogram to a PNG (no window).
              spektra --version                  Print the version.
              spektra --help                     Show this help.

            Add --json or --csv to any command for a machine-readable report,
            e.g. spektra scan Music --csv > report.csv

            Folders are analyzed in parallel. By default Spektra uses about 80% of
            the CPU cores; cap it with --jobs N (or -j N), e.g. spektra scan Music -j 4

            audit caches results per file (keyed by size + mtime) in the app data
            folder, so repeat runs only analyze new or changed files; --fresh re-analyzes.

            diff aligns the files automatically; pin an offset with --offset <ms>
            (positive = B later than A) and tune the verdict with --threshold-db <N>
            (default 40: the null depth needed to count as SAME).

            image options: -o <out.png>, --palette <name>, --gamma <g> (the app's
            Tightness), --floor <dB>, --fft <size>, --channel <n>, --columns <max
            width>. Palette and gamma default to your app settings; the rest to
            -120, 2048, mix, 2048. --palette also accepts custom palettes: JSON
            files dropped in %APPDATA%\Spektra\palettes or a palettes folder next
            to the app (see docs/cli.md for the format).

            Exit code is 1 on findings (report/scan: lossy or upsampled; audit:
            a transcode, an upsample, or corruption - an honest lossy file is
            not a problem; diff: the files differ), 2 on setup errors.
            Requires ffmpeg + ffprobe on PATH.
            """);
        return exitCode;
    }
}
