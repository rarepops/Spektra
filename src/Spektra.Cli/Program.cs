using Spektra.Core;

namespace Spektra.Cli;

/// Cross-platform command-line front end for Spektra's analysis engine. Writes
/// to stdout like a normal console program, so output pipes and redirects
/// cleanly. Exit code 1 when anything is judged likely lossy, 2 on setup errors.
internal static class Program
{
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

        return args[0] switch
        {
            "--report" or "report" => Report(ffmpeg, args[1..]),
            "--scan" or "scan" => Scan(ffmpeg, args[1..]),
            _ => Usage(1),
        };
    }

    private static int Report(FfmpegPaths ffmpeg, string[] paths)
    {
        if (paths.Length == 0)
        {
            Console.Error.WriteLine("spektra report: give one or more audio files.");
            return Usage(1);
        }
        var anyLossy = false;
        foreach (var path in paths)
        {
            var r = BandwidthReport.Analyze(ffmpeg, path);
            Console.WriteLine(Path.GetFileName(r.Path));
            if (r.Error is not null) { Console.WriteLine($"  error: {r.Error}"); continue; }
            Console.WriteLine("  " + r.Metadata!.ToDisplayLine(Path.GetFileName(r.Path)));
            Console.WriteLine("  " + r.Verdict!.Summary);
            if (r.Verdict.Kind == VerdictKind.Lossy) anyLossy = true;
        }
        return anyLossy ? 1 : 0;
    }

    private static int Scan(FfmpegPaths ffmpeg, string[] args)
    {
        if (args.Length == 0 || !Directory.Exists(args[0]))
        {
            Console.Error.WriteLine("spektra scan: give an existing folder to scan.");
            return Usage(1);
        }
        var root = args[0];
        var files = BandwidthReport.FindAudioFiles(root).ToList();
        Console.WriteLine($"Scanning {files.Count} audio file(s) under {root} ...");
        int lossless = 0, suspect = 0, lossy = 0, unknown = 0, errors = 0;
        foreach (var path in files)
        {
            var r = BandwidthReport.Analyze(ffmpeg, path);
            Console.WriteLine($"  {Tag(r)}  {Path.GetRelativePath(root, path)}");
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
            Spektra - audio bandwidth / transcode analyzer

            Usage:
              spektra report <file> [<file> ...]   Print each file's bandwidth verdict.
              spektra scan <folder>                Scan a folder and flag suspected transcodes.
              spektra --help                       Show this help.

            Exit code is 1 when anything is judged likely lossy, 2 on setup errors.
            Requires ffmpeg + ffprobe on PATH.
            """);
        return exitCode;
    }
}
