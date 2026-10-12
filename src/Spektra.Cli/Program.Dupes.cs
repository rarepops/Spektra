using Spektra.Core;

namespace Spektra.Cli;

internal static partial class Program
{
    private static int Dupes(FfmpegPaths ffmpeg, string[] args, OutFormat fmt, int jobs)
    {
        var fresh = args.Contains("--fresh");
        var roots = args.Where(a => a is not "--fresh").ToArray();
        var html = CliOptions.TakeHtml(ref roots);
        CliOptions.RejectUnknownFlags(roots);
        if (roots.Length == 0 || !roots.All(Directory.Exists))
        {
            Console.Error.WriteLine("spektra-cli dupes: give one or more existing folders.");
            return 2;
        }

        var cache = AuditCache.TryOpen(out var cacheError);
        if (cacheError is not null)
            Console.Error.WriteLine($"spektra-cli dupes: cache unavailable ({cacheError}); analyzing everything.");

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
                Console.Error.WriteLine($"spektra-cli dupes: {ex.Message}");
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
                $"sameness {g.Group.SamenessTier} · reclaim {Reporting.FormatBytes(g.ReclaimableBytes)}");
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
            $"reclaimable {Reporting.FormatBytes(result.ReclaimableBytes)} · {result.FilesScanned} scanned");
        return result.Groups.Count > 0 ? 1 : 0;
    }
}
