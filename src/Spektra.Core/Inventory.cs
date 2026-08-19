using System.Collections.Concurrent;

namespace Spektra.Core;

/// One file in an inventory: everything Spektra can learn without decoding.
/// Path is folder-relative with '/' separators, the same form AuditRow.File
/// takes, so an inventory and an audit of the same root join on this column.
/// Audio-only fields are null on everything else, which is how a folder's
/// cover.jpg earns a row without any column describing its neighbours.
public sealed record InventoryRow(
    string Path, string Name, string Ext, long SizeBytes, bool IsAudio,
    string? Codec, int? SampleRateHz, int? Channels, int? BitsPerSample,
    long? BitrateBps, double? DurationSeconds,
    string? Artist, string? AlbumArtist, string? Album, string? Title,
    int? Track, int? TrackTotal, int? Disc, int? DiscTotal, int? Year, string? Genre,
    bool? HasEmbeddedArt, string? ArtFormat, int? ArtWidth, int? ArtHeight,
    string? Error);

/// Lists one folder as machine-readable rows: the tree from FolderManifest,
/// plus one cheap ffprobe per audio file for its tags and cover art.
///
/// Never decodes, so this stays seconds-fast on any size of library. That is
/// also why it carries no bandwidth or integrity verdict: those need a full
/// decode and belong to `audit`, whose export joins to this one on Path.
public static class Inventory
{
    /// Walks one root and probes the audio under it. Exactly one root, because
    /// Path is relative to it and two roots would let two different files
    /// share a path string and collide in a join.
    public static IReadOnlyList<InventoryRow> Run(
        FfmpegPaths ffmpeg, string root, int jobs, CancellationToken ct = default)
    {
        var full = System.IO.Path.GetFullPath(root);
        // The manifest's walker, not a fourth one: cancellable, tolerant of
        // unreadable directories, and already never decoding.
        var tree = FolderManifest.Build(full, cache: null, ct);

        var files = new List<ManifestFile>();
        var unreadable = new List<ManifestFolder>();
        Collect(tree, files, unreadable);

        var probed = new ConcurrentDictionary<string, (AudioMetadata? Meta, string? Error)>(
            StringComparer.OrdinalIgnoreCase);
        var audio = files.Where(f => f.IsAudio).ToList();
        if (audio.Count > 0)
        {
            var reader = new FfprobeMetadataReader(ffmpeg.FfprobePath);
            Parallel.ForEach(
                audio,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs), CancellationToken = ct },
                f => probed[f.Path] = Probe(reader, f.Path, ct));
        }
        ct.ThrowIfCancellationRequested();

        var rows = new List<InventoryRow>(files.Count + unreadable.Count);
        foreach (var f in files)
        {
            var (meta, error) = f.IsAudio
                ? probed.GetValueOrDefault(f.Path)
                : (null, null);
            rows.Add(RowFor(full, f, meta, error));
        }
        // A folder that could not be read is the one directory that becomes a
        // row. Emitting nothing would let a permissions failure read as an
        // empty folder, which is the one absence a librarian must not trust.
        foreach (var d in unreadable)
            rows.Add(new InventoryRow(
                Reporting.RelativeFile(full, d.Path), d.Name, "", 0, false,
                null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, null,
                null, null, null, null,
                "folder could not be read"));
        return rows;
    }

    /// Depth-first in the manifest's display order, so an export reads like
    /// the tree does: a folder's subfolders before its own files.
    private static void Collect(ManifestFolder f, List<ManifestFile> files, List<ManifestFolder> unreadable)
    {
        if (f.Unreadable) { unreadable.Add(f); return; }
        foreach (var sub in f.Folders) Collect(sub, files, unreadable);
        files.AddRange(f.Files);
    }

    /// A file that cannot be probed becomes a row carrying the reason. Dropping
    /// it would let a damaged file read as absent, which is worse than a row
    /// with empty columns.
    ///
    /// Two different failures live here. A truncated file makes ffprobe return
    /// no streams and the reader throws. A ZERO-BYTE file does not: ffprobe
    /// exits 0 and reports a stream with sample rate 0 and channels 0, so the
    /// read succeeds and the row would claim a real track with a 0 Hz sample
    /// rate. A probe that parsed nothing is an error even though nothing threw.
    private static (AudioMetadata?, string?) Probe(FfprobeMetadataReader reader, string path, CancellationToken ct)
    {
        try
        {
            var meta = reader.Read(path, ct);
            return meta is { SampleRate: <= 0, Channels: <= 0 }
                ? (null, "no readable audio stream (the file is empty or not really audio)")
                : (meta, null);
        }
        catch (AudioDecodeException ex)
        {
            return (null, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, ex.Message);
        }
    }

    private static InventoryRow RowFor(string root, ManifestFile f, AudioMetadata? m, string? error) => new(
        Reporting.RelativeFile(root, f.Path),
        f.Name,
        System.IO.Path.GetExtension(f.Name).TrimStart('.').ToLowerInvariant(),
        f.SizeBytes,
        f.IsAudio,
        m?.Codec,
        m is null ? null : m.SampleRate,
        m is null ? null : m.Channels,
        m?.BitsPerSample,
        m?.BitRateBps,
        m is null ? null : m.Duration.TotalSeconds,
        m?.Artist, m?.AlbumArtist, m?.Album, m?.Title,
        m?.Track, m?.TrackTotal, m?.Disc, m?.DiscTotal, m?.Year, m?.Genre,
        m is null ? null : m.HasEmbeddedArt,
        m?.ArtFormat, m?.ArtWidth, m?.ArtHeight,
        error);
}
