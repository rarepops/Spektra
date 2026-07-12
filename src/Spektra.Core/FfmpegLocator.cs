namespace Spektra.Core;

public sealed record FfmpegPaths(string FfmpegPath, string FfprobePath);

public static class FfmpegLocator
{
    public static FfmpegPaths? Locate(IEnumerable<string> probeDirs, bool searchPath = true)
    {
        var exe = OperatingSystem.IsWindows() ? ".exe" : "";
        var dirs = probeDirs.ToList();
        if (searchPath)
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        foreach (var dir in dirs)
        {
            var ffmpeg = Path.Combine(dir, "ffmpeg" + exe);
            var ffprobe = Path.Combine(dir, "ffprobe" + exe);
            if (File.Exists(ffmpeg) && File.Exists(ffprobe))
                return new FfmpegPaths(ffmpeg, ffprobe);
        }
        return null;
    }

    /// The per-user directory the app downloads ffmpeg into
    /// (%LOCALAPPDATA%\Spektra\ffmpeg).
    public static string DownloadDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Spektra", "ffmpeg");

    /// Standard probe dirs for the app: exe folder, then the download dir, then
    /// PATH.
    public static FfmpegPaths? LocateDefault() => Locate([AppContext.BaseDirectory, DownloadDir]);

    /// A human label for where an ffmpeg binary was found, for the About window's
    /// diagnostics: "app folder" (next to the exe), "downloaded" (the per-user
    /// download dir), or "system" (anywhere else, i.e. found on PATH). Pure over
    /// its inputs so it can be tested without touching the real filesystem.
    public static string ClassifySource(string ffmpegPath, string appDir, string downloadDir)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(ffmpegPath)) ?? "";
        if (SameDir(dir, appDir)) return "app folder";
        if (SameDir(dir, downloadDir)) return "downloaded";
        return "system";
    }

    private static bool SameDir(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
