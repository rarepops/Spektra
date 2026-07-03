using System.Runtime.InteropServices;
using Spektra.Core;

namespace Spektra.App;

/// Headless command-line modes (no GUI): print a bandwidth verdict for files or
/// scan a folder for suspected transcodes. Exit code 1 when anything looks lossy
/// so it can gate scripts.
internal static class Cli
{
    public static bool IsCliMode(string[] args) => args.Length > 0 && args[0] is "--report" or "--scan";

    public static int Run(string[] args)
    {
        EnsureConsole();
        var ffmpeg = FfmpegLocator.LocateDefault();
        if (ffmpeg is null)
        {
            Console.Error.WriteLine(@"Spektra: ffmpeg/ffprobe not found (PATH or %LOCALAPPDATA%\Spektra\ffmpeg).");
            return 2;
        }
        return args[0] switch
        {
            "--report" => Report(ffmpeg, args.Skip(1).ToArray()),
            "--scan" => Scan(ffmpeg, args.Skip(1).ToArray()),
            _ => Usage(),
        };
    }

    private static int Report(FfmpegPaths ffmpeg, string[] paths)
    {
        if (paths.Length == 0) return Usage();
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
            Console.Error.WriteLine("Spektra --scan: provide an existing folder to scan.");
            return Usage();
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

    private static int Usage()
    {
        Console.WriteLine("Spektra command-line usage:");
        Console.WriteLine("  spektra --report <file> [<file> ...]   Print the bandwidth verdict for each file.");
        Console.WriteLine("  spektra --scan <folder>                Scan a folder and flag suspected transcodes.");
        Console.WriteLine("Exit code is 1 when anything is judged likely lossy.");
        return 1;
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    private const int AttachParentProcess = -1;

    private static void EnsureConsole()
    {
        if (OperatingSystem.IsWindows()) AttachConsole(AttachParentProcess);
    }
}
