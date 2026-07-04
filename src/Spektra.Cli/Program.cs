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

        var (fmt, rest) = TakeFormat(args[1..]);
        return args[0] switch
        {
            "--report" or "report" => Report(ffmpeg, rest, fmt),
            "--scan" or "scan" => Scan(ffmpeg, rest, fmt),
            "--check" or "check" => Check(ffmpeg, rest, fmt),
            "--audit" or "audit" => Audit(ffmpeg, rest, fmt),
            _ => Usage(1),
        };
    }

    private static (OutFormat fmt, string[] rest) TakeFormat(string[] args)
    {
        var fmt = OutFormat.Text;
        var rest = new List<string>();
        foreach (var a in args)
        {
            if (a is "--json") fmt = OutFormat.Json;
            else if (a is "--csv") fmt = OutFormat.Csv;
            else rest.Add(a);
        }
        return (fmt, rest.ToArray());
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

    private static int Report(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt)
    {
        var files = ResolveInputs(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("spektra report: give one or more audio files or a folder.");
            return Usage(1);
        }
        var reports = files.Select(f => BandwidthReport.Analyze(ffmpeg, f)).ToList();

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

    private static int Scan(FfmpegPaths ffmpeg, string[] args, OutFormat fmt)
    {
        if (args.Length == 0 || !Directory.Exists(args[0]))
        {
            Console.Error.WriteLine("spektra scan: give an existing folder to scan.");
            return Usage(1);
        }
        var root = args[0];
        var files = BandwidthReport.FindAudioFiles(root).ToList();
        var reports = files.Select(f => BandwidthReport.Analyze(ffmpeg, f)).ToList();

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

    private static int Check(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt)
    {
        var files = ResolveInputs(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("spektra check: give one or more audio files or a folder.");
            return Usage(1);
        }
        var rows = new List<IntegrityRow>();
        int ok = 0, suspect = 0, corrupt = 0;
        foreach (var path in files)
        {
            IntegrityReport? ir = null;
            string? err = null;
            try
            {
                var meta = new AnalysisSession(ffmpeg).ReadMetadata(path);
                ir = new IntegrityScanner(ffmpeg).Check(path, meta);
            }
            catch (Exception ex) when (ex is AudioDecodeException or IOException)
            {
                err = ex.Message;
            }
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

    private static int Audit(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt)
    {
        var files = ResolveInputs(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("spektra audit: give one or more audio files or a folder.");
            return Usage(1);
        }
        var rows = new List<AuditRow>();
        var problems = 0;
        foreach (var path in files)
        {
            var report = BandwidthReport.Analyze(ffmpeg, path);
            IntegrityReport? ir = null;
            string? ierr = null;
            if (report.Metadata is { } meta)
            {
                try { ir = new IntegrityScanner(ffmpeg).Check(path, meta); }
                catch (Exception ex) when (ex is AudioDecodeException or IOException) { ierr = ex.Message; }
            }
            rows.Add(Reporting.ToAuditRow(report, ir, ierr));

            if (report.Error is not null || report.Verdict?.Kind == VerdictKind.Lossy
                || ierr is not null || ir?.Status == IntegrityStatus.Corrupt) problems++;

            if (fmt == OutFormat.Text)
            {
                var bw = report.Error is not null ? "error"
                    : $"{report.Verdict!.Kind}{(report.Verdict.CutoffHz is { } hz ? $" {hz / 1000:0.0}k" : "")}";
                var integ = ierr is not null ? "ERROR" : ir?.Status.ToString() ?? "-";
                Console.WriteLine($"  bandwidth={bw,-18} integrity={integ,-8} {Path.GetFileName(path)}");
            }
        }
        if (fmt != OutFormat.Text) Emit(rows, fmt);
        else Console.WriteLine($"{Environment.NewLine}{files.Count} files, {problems} with problems.");
        return problems > 0 ? 1 : 0;
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
              spektra --help                     Show this help.

            Add --json or --csv to any command for a machine-readable report,
            e.g. spektra scan Music --csv > report.csv

            Exit code is 1 when anything is likely lossy or corrupt, 2 on setup errors.
            Requires ffmpeg + ffprobe on PATH.
            """);
        return exitCode;
    }
}
