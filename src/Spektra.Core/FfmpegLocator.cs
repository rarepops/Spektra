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

    /// Standard probe dirs for the app: exe folder, then %LOCALAPPDATA%\Spektra\ffmpeg,
    /// then PATH.
    public static FfmpegPaths? LocateDefault() => Locate(
    [
        AppContext.BaseDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spektra", "ffmpeg"),
    ]);
}
