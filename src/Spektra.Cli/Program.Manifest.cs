using Spektra.Core;

namespace Spektra.Cli;

internal static partial class Program
{
    // The GUI's Folder Manifest as a command: EVERYTHING in one folder with an
    // honest chip per file, codec/severity chips from the audit cache when a
    // file was analyzed before, never decoding anything.
    private static int Manifest(string[] paths, OutFormat fmt)
    {
        var html = CliOptions.TakeHtml(ref paths);
        CliOptions.RejectUnknownFlags(paths);
        if (paths.Length != 1 || !Directory.Exists(paths[0]))
        {
            Console.Error.WriteLine("spektra-cli manifest: give one existing folder.");
            return 2;
        }

        var cache = AuditCache.TryOpen(out var cacheError);
        if (cacheError is not null)
            Console.Error.WriteLine($"spektra-cli manifest: cache unavailable ({cacheError}); extension chips only.");

        ManifestFolder root;
        try { root = FolderManifest.Build(paths[0], cache); }
        finally { cache?.Dispose(); }

        if (root.Unreadable)
        {
            Console.Error.WriteLine($"spektra-cli manifest: could not read {paths[0]}.");
            return 2;
        }

        if (html is not null)
        {
            try { File.WriteAllText(html, HtmlReport.ManifestDocument(root, "Spektra Folder Manifest")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"spektra-cli manifest: {ex.Message}");
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
            Console.WriteLine($"{row.Kind,-6} {Reporting.FormatBytes(row.SizeBytes),10}  {row.Path}{severity}");
        }
        Console.WriteLine($"{rows.Count} file(s) · {root.Rollup} · {Reporting.FormatBytes(root.TotalBytes)}");
        return 0;
    }
}
