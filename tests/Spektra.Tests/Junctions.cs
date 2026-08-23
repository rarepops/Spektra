using System.Diagnostics;

namespace Spektra.Tests;

/// Creates NTFS junctions for the walker tests, through cmd's mklink builtin:
/// junctions, unlike symbolic links, need no privilege and no developer mode,
/// so they work on any runner. Windows only; callers self-skip elsewhere.
internal static class Junctions
{
    public static void Create(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in (string[])["/c", "mklink", "/J", link, target])
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"mklink /J failed: {stderr}{stdout}");
    }
}
