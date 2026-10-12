using Spektra.Core;

namespace Spektra.Cli;

internal static partial class Program
{
    private static int Report(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt, int jobs)
    {
        var files = ResolveInputs(paths);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("spektra-cli report: give one or more audio files or a folder.");
            return 2;
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
        return reports.Any(r => r.Verdict?.Kind is VerdictKind.Lossy or VerdictKind.Upsampled or VerdictKind.Mixed) ? 1 : 0;
    }

    private static int Scan(FfmpegPaths ffmpeg, string[] args, OutFormat fmt, int jobs)
    {
        CliOptions.RejectUnknownFlags(args);
        if (args.Length == 0 || !Directory.Exists(args[0]))
        {
            Console.Error.WriteLine("spektra-cli scan: give an existing folder to scan.");
            return 2;
        }
        var root = args[0];
        var files = BandwidthReport.FindAudioFiles(root).ToList();
        var reports = MapParallel(files, jobs, f => BandwidthReport.Analyze(ffmpeg, f));
        // One exit-code rule for both output paths: 1 when any file is likely
        // lossy or upsampled (computed once so the two branches can't drift).
        var findings = reports.Any(r => r.Verdict?.Kind is VerdictKind.Lossy or VerdictKind.Upsampled or VerdictKind.Mixed) ? 1 : 0;

        if (fmt != OutFormat.Text)
        {
            Emit(reports.Select(Reporting.ToBandwidthRow).ToList(), fmt);
            return findings;
        }

        Console.WriteLine($"Scanning {files.Count} audio file(s) under {root} ...");
        int lossless = 0, suspect = 0, lossy = 0, upsampled = 0, mixed = 0, unknown = 0, errors = 0;
        foreach (var r in reports)
        {
            Console.WriteLine($"  {Tag(r)}  {Path.GetRelativePath(root, r.Path)}");
            switch (r.Verdict?.Kind)
            {
                case VerdictKind.Lossless: lossless++; break;
                case VerdictKind.Suspicious: suspect++; break;
                case VerdictKind.Lossy: lossy++; break;
                case VerdictKind.Upsampled: upsampled++; break;
                case VerdictKind.Mixed: mixed++; break;
                default: if (r.Error is not null) errors++; else unknown++; break;
            }
        }
        Console.WriteLine(
            $"{Environment.NewLine}{files.Count} files: {lossless} lossless, {suspect} suspect, " +
            $"{lossy} likely lossy, {upsampled} upsampled, {mixed} mixed, {unknown} unknown, {errors} errors.");
        return findings;
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
            VerdictKind.Mixed => "[MIXED   ]",
            _ => "[UNKNOWN ]",
        };
        var cut = r.Verdict.CutoffHz is { } hz ? $" {hz / 1000:0.0}k".PadRight(6) : "      ";
        return kind + cut;
    }
}
