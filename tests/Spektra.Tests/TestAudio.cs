using System.Diagnostics;
using System.Globalization;
using Spektra.Core;

namespace Spektra.Tests;

/// Test audio derived from the committed fixtures with ffmpeg, in a temp folder
/// the calling test class creates and deletes, never in the repo.
internal static class TestAudio
{
    public static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;

    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// The path of a committed fixture, copied next to the test binaries.
    public static string Fixture(string name) => Path.Combine(Fixtures, name);

    /// Runs ffmpeg quietly, overwriting its output, and throws with its stderr on
    /// a nonzero exit.
    public static void Ffmpeg(params string[] args)
    {
        var psi = new ProcessStartInfo(Ff.FfmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in (string[])["-nostdin", "-v", "error", "-y", .. args])
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed ({p.ExitCode}): {stderr}");
    }

    /// A fixture played `times` times in a row into `target`, without re-encoding.
    /// `times` is 1 or more: 0 would pass a loop count of -1, which ffmpeg reads as
    /// looping forever.
    public static void Loop(string fixture, int times, string target)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(times, 1);
        Ffmpeg("-stream_loop", (times - 1).ToString(CultureInfo.InvariantCulture),
            "-i", Fixture(fixture), "-c", "copy", target);
    }
}
