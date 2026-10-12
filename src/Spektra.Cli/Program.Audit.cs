using Spektra.Core;

namespace Spektra.Cli;

internal static partial class Program
{
    private static int Audit(FfmpegPaths ffmpeg, string[] paths, OutFormat fmt, int jobs)
    {
        var fresh = paths.Contains("--fresh");
        paths = paths.Where(p => p != "--fresh").ToArray();
        var html = CliOptions.TakeHtml(ref paths);
        CliOptions.RejectUnknownFlags(paths);
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
            Console.Error.WriteLine("spektra-cli audit: give one or more audio files or a folder.");
            return 2;
        }

        var cache = AuditCache.TryOpen(out var cacheError);
        if (cacheError is not null)
            Console.Error.WriteLine($"spektra-cli audit: cache unavailable ({cacheError}); analyzing everything.");

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
                Console.Error.WriteLine($"spektra-cli audit: {ex.Message}");
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
}
