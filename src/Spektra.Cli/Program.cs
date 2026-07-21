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
        // UTF-8 output regardless of the console's legacy codepage: summaries
        // and compare lines carry middots and Δ, which the OEM codepage
        // mangles in redirects and pipes.
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; }
        catch (IOException) { /* no console to configure */ }

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            return Usage(args.Length == 0 ? 1 : 0);

        if (args[0] is "--version" or "-v")
        {
            Console.WriteLine($"spektra {Version()}");
            return 0;
        }

        // The manifest never decodes (it lists and reads cache), so it is the
        // one verb that must keep working without ffmpeg installed; it
        // dispatches before the gate.
        if (args[0] is "--manifest" or "manifest")
        {
            try
            {
                var (fmt, _, rest) = TakeOptions(args[1..]);
                return Manifest(rest, fmt);
            }
            catch (OptionException ex)
            {
                Console.Error.WriteLine($"spektra: {ex.Message}");
                return 2;
            }
        }

        var ffmpeg = FfmpegLocator.LocateDefault();
        if (ffmpeg is null)
        {
            Console.Error.WriteLine(
                "spektra: ffmpeg/ffprobe not found. Install ffmpeg and ensure it is on PATH.");
            return 2;
        }

        try
        {
            var (fmt, jobs, rest) = TakeOptions(args[1..]);
            return args[0] switch
            {
                "--report" or "report" => Report(ffmpeg, rest, fmt, jobs),
                "--scan" or "scan" => Scan(ffmpeg, rest, fmt, jobs),
                "--check" or "check" => Check(ffmpeg, rest, fmt, jobs),
                "--audit" or "audit" => Audit(ffmpeg, rest, fmt, jobs),
                "--dupes" or "dupes" => Dupes(ffmpeg, rest, fmt, jobs),
                "--loudness" or "loudness" => Loudness(ffmpeg, rest, fmt, jobs),
                "--diff" or "diff" => Diff(ffmpeg, rest, fmt),
                "--image" or "image" => Image(ffmpeg, rest, fmt),
                _ => Usage(1),
            };
        }
        catch (OptionException ex)
        {
            Console.Error.WriteLine($"spektra: {ex.Message}");
            return 2;
        }
    }

    // Default worker count: about 80% of the logical cores, leaving headroom for
    // the OS and for ffmpeg's own decode threads. Override with --jobs / -j.
    private static readonly int DefaultJobs = Math.Max(1, (int)(Environment.ProcessorCount * 0.8));

    // A recognized option flag was given a missing or invalid value. Carried out
    // to Main, which prints it and exits 2, instead of letting the flag and its
    // value fall through and be misread as file arguments (which produced a
    // baffling "give one audio file" error).
    private sealed class OptionException(string message) : Exception(message);

    // Consume the value following a recognized flag, or fail with a clear message.
    private static string OptionValue(string flag, string[] args, ref int i) =>
        i + 1 < args.Length ? args[++i] : throw new OptionException($"{flag} needs a value.");

    private static int IntOption(string flag, string[] args, ref int i, int min)
    {
        var v = OptionValue(flag, args, ref i);
        return int.TryParse(v, out var n) && n >= min ? n
            : throw new OptionException($"{flag} must be a whole number >= {min}, got '{v}'.");
    }

    private static double DoubleOption(string flag, string[] args, ref int i)
    {
        var v = OptionValue(flag, args, ref i);
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n
            : throw new OptionException($"{flag} must be a number, got '{v}'.");
    }

    private static float FloatOption(string flag, string[] args, ref int i)
    {
        var v = OptionValue(flag, args, ref i);
        return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n
            : throw new OptionException($"{flag} must be a number, got '{v}'.");
    }

    /// Pulls "--html <path>" out of a verb's argument list; null when absent.
    private static string? TakeHtml(ref string[] args)
    {
        var i = Array.IndexOf(args, "--html");
        if (i < 0) return null;
        if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
            throw new OptionException("--html needs a file path.");
        var path = args[i + 1];
        args = [.. args[..i], .. args[(i + 2)..]];
        return path;
    }

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
            else if (a is "--jobs" or "-j") jobs = IntOption(a, args, ref i, min: 1);
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
        // One exit-code rule for both output paths: 1 when any file is likely
        // lossy or upsampled (computed once so the two branches can't drift).
        var findings = reports.Any(r => r.Verdict?.Kind is VerdictKind.Lossy or VerdictKind.Upsampled) ? 1 : 0;

        if (fmt != OutFormat.Text)
        {
            Emit(reports.Select(Reporting.ToBandwidthRow).ToList(), fmt);
            return findings;
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
        return findings;
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
        var html = TakeHtml(ref paths);
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

        // Folder audits report folder-relative paths (matching `scan`), so a
        // row in a deep tree can be located; explicit file args keep the name.
        AuditRow RowFor(AuditEntry r) => folder is null
            ? r.Row
            : r.Row with { File = Reporting.RelativeFile(folder, r.Target.Path) };

        var rows = results.Select(RowFor).ToList();
        if (html is not null)
        {
            try { File.WriteAllText(html, HtmlReport.AuditDocument(rows, "Spektra audit")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"spektra audit: {ex.Message}");
                return 2;
            }
        }

        if (fmt != OutFormat.Text)
        {
            Emit(rows, fmt);
            return results.Any(r => r.HasProblem) ? 1 : 0;
        }

        foreach (var row in rows)
        {
            var bw = row.Error is not null ? "error"
                : $"{row.Bandwidth}{(row.CutoffHz is { } hz ? $" {hz / 1000:0.0}k" : "")}";
            var integ = row.Error is not null ? "ERROR" : row.Integrity;
            Console.WriteLine($"  bandwidth={bw,-18} integrity={integ,-8} {row.File}");
        }
        Console.WriteLine($"{Environment.NewLine}{targets.Length} files, {results.Count(r => r.HasProblem)} with problems.");
        return results.Any(r => r.HasProblem) ? 1 : 0;
    }

    private static int Dupes(FfmpegPaths ffmpeg, string[] args, OutFormat fmt, int jobs)
    {
        var fresh = args.Contains("--fresh");
        var roots = args.Where(a => a is not "--fresh").ToArray();
        var html = TakeHtml(ref roots);
        if (roots.Length == 0 || !roots.All(Directory.Exists))
        {
            Console.Error.WriteLine("spektra dupes: give one or more existing folders.");
            return 2;
        }

        AuditCache? cache = null;
        try { cache = AuditCache.Open(AuditCache.DefaultPath); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"spektra dupes: cache unavailable ({ex.Message}); analyzing everything.");
        }

        DupesResult result;
        try
        {
            var progress = new Progress<DupesProgress>(p =>
            {
                if (!Console.IsErrorRedirected)
                    Console.Error.Write($"\r{p.Phase} {p.Done}/{p.Total}    ");
            });
            result = DuplicateScan.Run(ffmpeg, roots, jobs, cache, fresh, progress);
            if (!Console.IsErrorRedirected) Console.Error.Write("\r                        \r");
        }
        finally { cache?.Dispose(); }

        if (html is not null)
        {
            try { File.WriteAllText(html, HtmlReport.DupesDocument(result, "Spektra Duplicate Detective")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"spektra dupes: {ex.Message}");
                return 2;
            }
        }

        if (fmt != OutFormat.Text)
        {
            Emit(DuplicateScan.ToRows(result), fmt);
            return result.Groups.Count > 0 ? 1 : 0;
        }

        foreach (var g in result.Groups)
        {
            Console.WriteLine(
                $"Group {g.Group.Id} · {g.Group.Label} · {g.Group.Members.Count} files · " +
                $"sameness {g.Group.SamenessTier} · reclaim {Bytes(g.ReclaimableBytes)}");
            foreach (var m in g.Group.Members)
            {
                var row = g.Rows[m.Path];
                var mark = g.Quality.Winners.Contains(m.Path) ? "*" : " ";
                var audio = m.FoundByAudio ? "  found by audio" : "";
                Console.WriteLine(
                    $"  {mark} {m.Path}  [{row.Codec} · {row.Bandwidth}" +
                    $"{(row.CutoffHz is { } c ? $" {c / 1000.0:0.0}k" : "")} · {row.Integrity}] " +
                    $"sameness {m.Sameness:0.00}{audio}");
            }
            Console.WriteLine($"    quality {g.Quality.Confidence}: {g.Quality.Reason}");
        }
        foreach (var na in result.NotAnalyzed)
            Console.WriteLine($"  ! not analyzed: {na.Path} ({na.Reason})");
        Console.WriteLine(
            $"{result.Groups.Count} group(s) · {result.Groups.Sum(g => g.Group.Members.Count)} files · " +
            $"reclaimable {Bytes(result.ReclaimableBytes)} · {result.FilesScanned} scanned");
        return result.Groups.Count > 0 ? 1 : 0;
    }

    private static string Bytes(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.0} MB",
        _ => $"{b / 1024.0:0.0} KB",
    };

    // The GUI's Folder Manifest as a command: EVERYTHING in one folder with an
    // honest chip per file, codec/severity chips from the audit cache when a
    // file was analyzed before, never decoding anything.
    private static int Manifest(string[] paths, OutFormat fmt)
    {
        var html = TakeHtml(ref paths);
        if (paths.Length != 1 || !Directory.Exists(paths[0]))
        {
            Console.Error.WriteLine("spektra manifest: give one existing folder.");
            return 2;
        }

        AuditCache? cache = null;
        try { cache = AuditCache.Open(AuditCache.DefaultPath); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"spektra manifest: cache unavailable ({ex.Message}); extension chips only.");
        }

        ManifestFolder root;
        try { root = FolderManifest.Build(paths[0], cache); }
        finally { cache?.Dispose(); }

        if (root.Unreadable)
        {
            Console.Error.WriteLine($"spektra manifest: could not read {paths[0]}.");
            return 2;
        }

        if (html is not null)
        {
            try { File.WriteAllText(html, HtmlReport.ManifestDocument(root, "Spektra Folder Manifest")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"spektra manifest: {ex.Message}");
                return 2;
            }
        }

        var rows = FolderManifest.ToRows(root);
        if (fmt != OutFormat.Text)
        {
            Emit(rows, fmt);
            return 0;
        }

        foreach (var row in rows)
        {
            var severity = row.Severity is { } s ? $" · {s}" : "";
            Console.WriteLine($"{row.Kind,-6} {Bytes(row.SizeBytes),10}  {row.Path}{severity}");
        }
        Console.WriteLine($"{rows.Count} file(s) · {root.Rollup} · {Bytes(root.TotalBytes)}");
        return 0;
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
        // Loudness has no "problem" verdict; the only failure signal is a file
        // that could not be measured (mirrors check/audit: any error -> 1).
        return computed.Any(c => c.err is not null) ? 1 : 0;
    }

    private static int Diff(FfmpegPaths ffmpeg, string[] args, OutFormat fmt)
    {
        double? offsetMs = null;
        var thresholdDb = 40f;
        var files = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--offset") offsetMs = DoubleOption(a, args, ref i);
            else if (a is "--threshold-db") thresholdDb = FloatOption(a, args, ref i);
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
            if (a is "-o" or "--out") outPath = OptionValue(a, args, ref i);
            else if (a is "--palette") paletteName = OptionValue(a, args, ref i);
            else if (a is "--gamma")
            {
                var g = DoubleOption(a, args, ref i);
                gamma = g > 0 ? g : throw new OptionException($"--gamma must be greater than 0, got '{g}'.");
            }
            else if (a is "--floor") options = options with { FloorDb = FloatOption(a, args, ref i) };
            else if (a is "--fft") options = options with { WindowSize = IntOption(a, args, ref i, min: 16) };
            else if (a is "--channel") options = options with { Channel = IntOption(a, args, ref i, min: 1) - 1 };
            else if (a is "--columns") options = options with { MaxColumns = IntOption(a, args, ref i, min: 1) };
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
              spektra dupes <folder> ...         Find duplicate songs; mark the best copy (cached).
              spektra manifest <folder>          List EVERYTHING in a folder with type chips (no decoding).
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

            Exit code is 1 on findings (report/scan: lossy or upsampled; check:
            corrupt files; audit: a transcode, an upsample, or corruption - an
            honest lossy file is not a problem; dupes: one or more duplicate
            groups found; loudness: a file could not be measured; diff: the
            files differ), 2 on setup errors (including an unknown or
            malformed option; dupes: no existing folder given).
            Requires ffmpeg + ffprobe on PATH.
            """);
        return exitCode;
    }
}
