namespace Spektra.Core;

/// One file in a listed folder: Kind is the chip label (the cached codec when
/// the audit cache knows this exact file, its extension otherwise), Severity
/// is the cached verdict severity (null when undecorated).
public sealed record ManifestFile(string Name, string Path, string Kind, RowSeverity? Severity, long SizeBytes);

/// One folder level: subfolders first, then files, both sorted; Rollup
/// summarizes every descendant file by chip label ("2 flac · 1 jpg · 1 nfo",
/// "empty", or "unreadable").
public sealed record ManifestFolder(
    string Name, string Path, IReadOnlyList<ManifestFolder> Folders,
    IReadOnlyList<ManifestFile> Files, string Rollup, bool Unreadable);

/// Flat export row, depth-first in display order (a folder's subfolder files
/// come before its own files, matching the tree read top to bottom).
public sealed record ManifestRow(string Path, string Name, string Kind, string? Severity, long SizeBytes);

/// Folder Manifest: list EVERYTHING under one folder with an honest type chip per
/// file, never decoding audio. Read-only by identity: no file operation
/// exists here or ever will.
public static class FolderManifest
{
    /// Enumerates one root into a display-ready tree. A directory that cannot
    /// be enumerated (denied, vanished, nonexistent) becomes an Unreadable
    /// node and the walk continues; a bad root yields one Unreadable root.
    public static ManifestFolder Build(string root, AuditCache? cache)
    {
        string full;
        try
        {
            full = Path.GetFullPath(root);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A root that cannot even be resolved is the same story as one
            // that cannot be enumerated: one unreadable node, never a throw.
            return new ManifestFolder(root, root, [], [], "unreadable", Unreadable: true);
        }
        return BuildNode(full, NameOf(full), cache).Node;
    }

    public static IReadOnlyList<ManifestRow> ToRows(ManifestFolder root)
    {
        var rows = new List<ManifestRow>();
        Walk(root);
        return rows;

        void Walk(ManifestFolder f)
        {
            foreach (var sub in f.Folders) Walk(sub);
            foreach (var file in f.Files)
                rows.Add(new ManifestRow(file.Path, file.Name, file.Kind, file.Severity?.ToString(), file.SizeBytes));
        }
    }

    private static (ManifestFolder Node, Dictionary<string, int> Counts) BuildNode(
        string path, string name, AuditCache? cache)
    {
        string[] dirs, files;
        try
        {
            dirs = Directory.GetDirectories(path);
            files = Directory.GetFiles(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (new ManifestFolder(name, path, [], [], "unreadable", Unreadable: true), []);
        }
        Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var folders = new ManifestFolder[dirs.Length];
        for (var i = 0; i < dirs.Length; i++)
        {
            var (child, childCounts) = BuildNode(dirs[i], NameOf(dirs[i]), cache);
            folders[i] = child;
            foreach (var (kind, n) in childCounts)
                counts[kind] = counts.GetValueOrDefault(kind) + n;
        }

        var manifestFiles = new ManifestFile[files.Length];
        for (var i = 0; i < files.Length; i++)
        {
            manifestFiles[i] = BuildFile(files[i], cache);
            counts[manifestFiles[i].Kind] = counts.GetValueOrDefault(manifestFiles[i].Kind) + 1;
        }

        return (new ManifestFolder(name, path, folders, manifestFiles, Rollup(counts), Unreadable: false), counts);
    }

    private static ManifestFile BuildFile(string path, AuditCache? cache)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path);
        var kind = ext.Length > 1 ? ext[1..].ToLowerInvariant() : "none";
        long size = 0;
        try
        {
            var info = new FileInfo(path);
            size = info.Length;
            if (cache is not null
                && BandwidthReport.AudioExtensions.Contains(ext.ToLowerInvariant())
                && cache.TryGet(new AuditTarget(path, info.Length, info.LastWriteTimeUtc.Ticks)) is { } hit)
                return new ManifestFile(
                    name, path, hit.Row.Codec?.ToLowerInvariant() ?? kind,
                    FolderAudit.RowSeverityOf(hit.Row), size);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // stat or cache hiccup mid-walk: keep the extension chip and a zero size
        }
        return new ManifestFile(name, path, kind, Severity: null, size);
    }

    private static string Rollup(Dictionary<string, int> counts) =>
        counts.Count == 0
            ? "empty"
            : string.Join(" · ", counts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Value} {kv.Key}"));

    private static string NameOf(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return name.Length == 0 ? path : name;
    }
}
