using System.IO.Enumeration;

namespace Spektra.Core;

/// One file's headless bandwidth analysis: metadata + verdict, or an error.
public sealed record FileReport(string Path, AudioMetadata? Metadata, LosslessVerdict? Verdict, string? Error);

/// Headless (no-UI) bandwidth analysis for a single file or a whole folder.
/// Reuses the same decode + cutoff pipeline the GUI uses.
public static class BandwidthReport
{
    public static readonly string[] AudioExtensions =
    [
        ".flac", ".mp3", ".wav", ".ogg", ".opus", ".m4a",
        ".aac", ".wma", ".ape", ".wv", ".aiff", ".aif", ".alac",
    ];

    public static FileReport Analyze(FfmpegPaths ffmpeg, string path, int windowSize = 2048, CancellationToken ct = default)
    {
        try
        {
            var session = new AnalysisSession(ffmpeg);
            var meta = session.ReadMetadata(path, ct);
            var settings = new SpectrogramSettings(WindowSize: windowSize);
            var columns = session.AnalyzeColumns(path, meta, settings, ct).ToList();
            return new FileReport(path, meta, ProvenanceScan.Analyze(columns, meta), null);
        }
        catch (Exception ex) when (ex is AudioDecodeException or IOException or InvalidOperationException)
        {
            return new FileReport(path, null, null, ex.Message);
        }
    }

    public static IEnumerable<string> FindAudioFiles(string folder, bool recursive = true) =>
        FindAudioFileInfos(folder, recursive).Select(f => f.FullName);

    /// The same walk as FindAudioFiles, handing back the FileInfo the
    /// enumeration already filled in. Reading Length/LastWriteTimeUtc off these
    /// is free, where `new FileInfo(path)` on a path from the walk re-stats the
    /// file (measured 30x slower over 2000 local files, and far worse on a
    /// share, where every stat is a round trip).
    /// Returns fully-qualified paths, so callers keying a cache on them must
    /// canonicalize the folder first, exactly as FindAudioFiles' callers do.
    public static IEnumerable<FileInfo> FindAudioFileInfos(string folder, bool recursive = true) =>
        new FileSystemEnumerable<FileInfo>(
            folder,
            (ref FileSystemEntry entry) => (FileInfo)entry.ToFileSystemInfo(),
            WalkOptions(recursive))
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory && IsAudioName(entry.FileName),
            // A junction or directory symlink inside the walk is never entered:
            // following one widens the scan to files the user never chose,
            // lists the same file under two names, and a link cycle recurses
            // until the path length blows. Descent only: a link given AS the
            // root still scans, since this predicate never sees the root, and
            // reparse-point FILES (OneDrive placeholders) are ordinary entries
            // here, not recursion candidates.
            ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                (entry.Attributes & FileAttributes.ReparsePoint) == 0,
        }
        .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase);

    private static bool IsAudioName(ReadOnlySpan<char> name)
    {
        var ext = Path.GetExtension(name);
        foreach (var audio in AudioExtensions)
            if (ext.Equals(audio, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // IgnoreInaccessible skips a permission-denied subfolder (a drive root's
    // System Volume Information, a mixed-permission share) instead of throwing
    // and aborting the whole walk. AttributesToSkip = 0 keeps the old
    // SearchOption behavior of including hidden/system files (the default here
    // would drop them); links are handled by the recurse predicate above, not
    // by skipping reparse-point entries wholesale.
    private static EnumerationOptions WalkOptions(bool recursive) => new()
    {
        RecurseSubdirectories = recursive,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };
}
