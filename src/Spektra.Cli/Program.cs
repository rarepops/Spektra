using Spektra.Core;

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
        return reports.Any(r => r.Verdict?.Kind == VerdictKind.Lossy) ? 1 : 0;
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
            return reports.Any(r => r.Verdict?.Kind == VerdictKind.Lossy) ? 1 : 0;
        }

        Console.WriteLine($"Scanning {files.Count} audio file(s) under {root} ...");
        int lossless = 0, suspect = 0, lossy = 0, unknown = 0, errors = 0;
        foreach (var r in reports)
        {
            Console.WriteLine($"  {Tag(r)}  {Path.GetRelativePath(root, r.Path)}");
            switch (r.Verdict?.Kind)
            {
                case VerdictKind.Lossless: lossless++; break;
                case VerdictKind.Suspicious: suspect++; break;
                case VerdictKind.Lossy: lossy++; break;
                default: if (r.Error is not null) errors++; else unknown++; break;
            }
        }
        Console.WriteLine(
            $"{Environment.NewLine}{files.Count} files: {lossless} lossless, {suspect} suspect, " +
            $"{lossy} likely lossy, {unknown} unknown, {errors} errors.");
        return lossy > 0 ? 1 : 0;
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
        var files = ResolveInputs(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("spektra audit: give one or more audio files or a folder.");
            return Usage(1);
        }
        var computed = MapParallel(files, jobs, path =>
        {
            var report = BandwidthReport.Analyze(ffmpeg, path);
            IntegrityReport? ir = null;
            string? ierr = null;
            if (report.Metadata is { } meta)
            {
                try { ir = new IntegrityScanner(ffmpeg).Check(path, meta); }
                catch (Exception ex) when (ex is AudioDecodeException or IOException) { ierr = ex.Message; }
            }
            return (report, ir, ierr);
        });

        var rows = new List<AuditRow>();
        var problems = 0;
        foreach (var (report, ir, ierr) in computed)
        {
            rows.Add(Reporting.ToAuditRow(report, ir, ierr));

            if (report.Error is not null || report.Verdict?.Kind == VerdictKind.Lossy
                || ierr is not null || ir?.Status == IntegrityStatus.Corrupt) problems++;

            if (fmt == OutFormat.Text)
            {
                var bw = report.Error is not null ? "error"
                    : $"{report.Verdict!.Kind}{(report.Verdict.CutoffHz is { } hz ? $" {hz / 1000:0.0}k" : "")}";
                var integ = ierr is not null ? "ERROR" : ir?.Status.ToString() ?? "-";
                Console.WriteLine($"  bandwidth={bw,-18} integrity={integ,-8} {Path.GetFileName(report.Path)}");
            }
        }
        if (fmt != OutFormat.Text) Emit(rows, fmt);
        else Console.WriteLine($"{Environment.NewLine}{files.Count} files, {problems} with problems.");
        return problems > 0 ? 1 : 0;
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

    private static string Tag(FileReport r)
    {
        if (r.Error is not null) return "[ERROR   ]      ";
        var kind = r.Verdict!.Kind switch
        {
            VerdictKind.Lossless => "[LOSSLESS]",
            VerdictKind.Suspicious => "[SUSPECT ]",
            VerdictKind.Lossy => "[LOSSY   ]",
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
              spektra audit <file|folder> ...    Bandwidth + integrity together.
              spektra loudness <file|folder> ... Loudness (LUFS), true peak, and dynamics.
              spektra --help                     Show this help.

            Add --json or --csv to any command for a machine-readable report,
            e.g. spektra scan Music --csv > report.csv

            Folders are analyzed in parallel. By default Spektra uses about 80% of
            the CPU cores; cap it with --jobs N (or -j N), e.g. spektra scan Music -j 4

            Exit code is 1 when anything is likely lossy or corrupt, 2 on setup errors.
            Requires ffmpeg + ffprobe on PATH.
            """);
        return exitCode;
    }
}
