using System.Runtime.InteropServices;

namespace Spektra.Core;

/// One file attached to a GitHub release: its name, its download URL (already
/// fenced to this repository by UpdateChecker.Evaluate), and its size in bytes,
/// 0 when the payload did not say.
public sealed record ReleaseAsset(string Name, string Url, long Size);

public enum ReleasePlatform { Windows, Linux, MacOS, Other }

/// What the running copy is, as far as choosing a release file goes.
/// `Installed` means the executable sits under a Program Files folder, which
/// is where the MSI puts it and nowhere a portable copy would be run from.
public sealed record ReleaseTarget(ReleasePlatform Platform, bool Arm64, bool Installed)
{
    public static ReleaseTarget Current() => new(
        OperatingSystem.IsWindows() ? ReleasePlatform.Windows
        : OperatingSystem.IsLinux() ? ReleasePlatform.Linux
        : OperatingSystem.IsMacOS() ? ReleasePlatform.MacOS
        : ReleasePlatform.Other,
        RuntimeInformation.OSArchitecture == Architecture.Arm64,
        IsInstalled(AppContext.BaseDirectory,
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ]));

    /// Pure string test, so it behaves the same on every OS: `exeDir` counts as
    /// installed when it is one of `programFilesDirs` or sits below one. Case
    /// and a trailing separator on either side are ignored (these are Windows
    /// paths); blank entries, which GetFolderPath answers off Windows, never
    /// match.
    public static bool IsInstalled(string exeDir, IEnumerable<string> programFilesDirs)
    {
        var exe = Trim(exeDir);
        foreach (var dir in programFilesDirs)
        {
            var pf = Trim(dir);
            if (pf.Length == 0 || exe.Length < pf.Length) continue;
            if (!exe.StartsWith(pf, StringComparison.OrdinalIgnoreCase)) continue;
            if (exe.Length == pf.Length || exe[pf.Length] is '\\' or '/') return true;
        }
        return false;
    }

    private static string Trim(string path) => path.Trim().TrimEnd('\\', '/');
}

/// Chooses which release file a running copy should download.
public static class ReleaseAssets
{
    public const string ChecksumsName = "SHA256SUMS.txt";

    /// The one file a copy running on `target` should download, or null when
    /// the release has none for it. Matched by name shape, never by building a
    /// name from the version, so a renamed asset reads as "nothing to download"
    /// rather than a wrong file. Linux and macOS get the command-line tool: the
    /// desktop app is only published for Windows.
    public static ReleaseAsset? Pick(IReadOnlyList<ReleaseAsset> assets, ReleaseTarget target) =>
        target.Platform switch
        {
            ReleasePlatform.Windows when target.Installed =>
                First(assets, n => Desktop(n) && Ends(n, "-Setup.msi")),
            ReleasePlatform.Windows =>
                First(assets, n => Desktop(n) && Ends(n, "-win-x64.zip")),
            ReleasePlatform.Linux =>
                First(assets, n => Cli(n) && Ends(n, "-linux-x64.zip")),
            ReleasePlatform.MacOS =>
                First(assets, n => Cli(n) && Ends(n, target.Arm64 ? "-osx-arm64.zip" : "-osx-x64.zip")),
            _ => null,
        };

    /// The release's checksum list, or null when it has none.
    public static ReleaseAsset? Checksums(IReadOnlyList<ReleaseAsset> assets) =>
        First(assets, n => n.Equals(ChecksumsName, StringComparison.OrdinalIgnoreCase));

    // "Spektra-<version>-..." is the desktop app; "spektra-cli-<version>-..." the tool.
    private static bool Cli(string name) => name.StartsWith("spektra-cli-", StringComparison.OrdinalIgnoreCase);
    private static bool Desktop(string name) => name.StartsWith("Spektra-", StringComparison.OrdinalIgnoreCase) && !Cli(name);
    private static bool Ends(string name, string suffix) => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    private static ReleaseAsset? First(IReadOnlyList<ReleaseAsset> assets, Func<string, bool> match)
    {
        foreach (var asset in assets)
            if (match(asset.Name)) return asset;
        return null;
    }
}
