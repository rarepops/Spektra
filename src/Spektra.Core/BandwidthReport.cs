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
            return new FileReport(path, meta, CutoffAnalyzer.Analyze(columns, meta.SampleRate), null);
        }
        catch (Exception ex) when (ex is AudioDecodeException or IOException or InvalidOperationException)
        {
            return new FileReport(path, null, null, ex.Message);
        }
    }

    public static IEnumerable<string> FindAudioFiles(string folder, bool recursive = true) =>
        // IgnoreInaccessible skips a permission-denied subfolder (a drive root's
        // System Volume Information, a mixed-permission share) instead of throwing
        // and aborting the whole walk. AttributesToSkip = 0 keeps the old
        // SearchOption behavior of including hidden/system files (the default here
        // would drop them).
        Directory.EnumerateFiles(folder, "*",
                new EnumerationOptions
                {
                    RecurseSubdirectories = recursive,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0,
                })
            .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
}
