namespace Spektra.Core;

/// One file in a listed folder: Kind is the chip label (the cached codec when
/// the audit cache knows this exact file, its extension otherwise), Severity
/// is the cached verdict severity (null when undecorated).
/// IsAudio is extension membership in the audit pipeline's set (NOT the Kind
/// chip, which shows the real codec once a file is analyzed): it answers
/// "would FindAudioFiles pick this up", so the UI can disable audio-only
/// verbs on everything else.
public sealed record ManifestFile(string Name, string Path, string Kind, RowSeverity? Severity, long SizeBytes, bool IsAudio);

/// One folder level: subfolders first, then files, both sorted; TotalBytes is
/// the recursive size of every descendant file; Rollup summarizes them by
/// chip label ("2 flac · 1 jpg · 1 nfo", "empty", or "unreadable").
public sealed record ManifestFolder(
    string Name, string Path, IReadOnlyList<ManifestFolder> Folders,
    IReadOnlyList<ManifestFile> Files, long TotalBytes, string Rollup, bool Unreadable);

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
    /// Cancellation is cooperative per directory and surfaces as
    /// OperationCanceledException, the one exception Build lets escape.
    public static ManifestFolder Build(string root, AuditCache? cache, CancellationToken ct = default)
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
            return new ManifestFolder(root, root, [], [], 0, "unreadable", Unreadable: true);
        }
        return BuildNode(full, NameOf(full), cache, ct).Node;
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

    /// Splits filter box text into normalized kind tokens: spaces, commas and
    /// semicolons separate; "*.flac", ".FLAC" and "flac" all mean "flac".
    public static IReadOnlyList<string> ParseKinds(string? text) =>
        [.. (text ?? "")
            .Split([' ', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.TrimStart('*').TrimStart('.').ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()];

    /// Narrows a built tree to the given kinds. A file stays when the filter
    /// matches its chip kind OR its real extension (a transcode's chip says
    /// mp3 while the file is named .flac; both queries should find it).
    /// Folders left with nothing are pruned, except the root and unreadable
    /// nodes; rollups are recomputed over what remains.
    public static ManifestFolder Filter(ManifestFolder root, IReadOnlyCollection<string> kinds)
    {
        var set = kinds as IReadOnlySet<string> ?? kinds.ToHashSet(StringComparer.Ordinal);
        return FilterNode(root, set, isRoot: true).Node!;
    }

    private static (ManifestFolder? Node, Dictionary<string, int> Counts) FilterNode(
        ManifestFolder f, IReadOnlySet<string> kinds, bool isRoot)
    {
        if (f.Unreadable) return (f, []);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var folders = new List<ManifestFolder>();
        foreach (var sub in f.Folders)
        {
            var (child, childCounts) = FilterNode(sub, kinds, isRoot: false);
            if (child is null) continue;
            folders.Add(child);
            foreach (var (kind, n) in childCounts)
                counts[kind] = counts.GetValueOrDefault(kind) + n;
        }

        var files = f.Files.Where(file => Matches(file, kinds)).ToList();
        foreach (var file in files)
            counts[file.Kind] = counts.GetValueOrDefault(file.Kind) + 1;

        if (!isRoot && folders.Count == 0 && files.Count == 0) return (null, counts);
        return (f with
        {
            Folders = folders,
            Files = files,
            TotalBytes = folders.Sum(x => x.TotalBytes) + files.Sum(x => x.SizeBytes),
            Rollup = Rollup(counts),
        }, counts);
    }

    private static bool Matches(ManifestFile file, IReadOnlySet<string> kinds)
    {
        if (kinds.Contains(file.Kind)) return true;
        var ext = Path.GetExtension(file.Name);
        return kinds.Contains(ext.Length > 1 ? ext[1..].ToLowerInvariant() : "none");
    }

    private static (ManifestFolder Node, Dictionary<string, int> Counts) BuildNode(
        string path, string name, AuditCache? cache, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string[] dirs, files;
        try
        {
            dirs = Directory.GetDirectories(path);
            files = Directory.GetFiles(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (new ManifestFolder(name, path, [], [], 0, "unreadable", Unreadable: true), []);
        }
        Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var folders = new ManifestFolder[dirs.Length];
        for (var i = 0; i < dirs.Length; i++)
        {
            var (child, childCounts) = BuildNode(dirs[i], NameOf(dirs[i]), cache, ct);
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

        var totalBytes = folders.Sum(f => f.TotalBytes) + manifestFiles.Sum(f => f.SizeBytes);
        return (new ManifestFolder(name, path, folders, manifestFiles, totalBytes, Rollup(counts), Unreadable: false), counts);
    }

    private static ManifestFile BuildFile(string path, AuditCache? cache)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path);
        var kind = ext.Length > 1 ? ext[1..].ToLowerInvariant() : "none";
        var isAudio = BandwidthReport.AudioExtensions.Contains(ext.ToLowerInvariant());
        long size = 0;
        try
        {
            var info = new FileInfo(path);
            size = info.Length;
            if (cache is not null && isAudio
                && cache.TryGet(new AuditTarget(path, info.Length, info.LastWriteTimeUtc.Ticks)) is { } hit)
                return new ManifestFile(
                    name, path, hit.Row.Codec?.ToLowerInvariant() ?? kind,
                    FolderAudit.RowSeverityOf(hit.Row), size, isAudio);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // stat or cache hiccup mid-walk: keep the extension chip and a zero size
        }
        return new ManifestFile(name, path, kind, Severity: null, size, isAudio);
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
