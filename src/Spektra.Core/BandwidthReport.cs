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
        Directory.EnumerateFiles(folder, "*", WalkOptions(recursive))
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    /// The same walk as FindAudioFiles, but handing back the FileInfo the
    /// enumeration already filled in. Reading Length/LastWriteTimeUtc off these
    /// is free, where `new FileInfo(path)` on a path from the walk re-stats the
    /// file (measured 30x slower over 2000 local files, and far worse on a
    /// share, where every stat is a round trip).
    /// Returns fully-qualified paths, so callers keying a cache on them must
    /// canonicalize the folder first, exactly as FindAudioFiles' callers do.
    public static IEnumerable<FileInfo> FindAudioFileInfos(string folder, bool recursive = true) =>
        new DirectoryInfo(folder).EnumerateFiles("*", WalkOptions(recursive))
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f.Name).ToLowerInvariant()))
            .OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase);

    // IgnoreInaccessible skips a permission-denied subfolder (a drive root's
    // System Volume Information, a mixed-permission share) instead of throwing
    // and aborting the whole walk. AttributesToSkip = 0 keeps the old
    // SearchOption behavior of including hidden/system files (the default here
    // would drop them).
    private static EnumerationOptions WalkOptions(bool recursive) => new()
    {
        RecurseSubdirectories = recursive,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };
}
