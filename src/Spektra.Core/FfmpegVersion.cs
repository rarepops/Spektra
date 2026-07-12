using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Spektra.Core;

/// Reads the version banner ffmpeg prints for `-version`. The first stdout line
/// looks like "ffmpeg version &lt;token&gt; Copyright ...", where the token varies by
/// build (a bare "6.1.1", a gyan "8.1.2-essentials_build-www.gyan.dev", or a git
/// "n7.1-11-g..."). The parse is kept separate from the launch so it can be
/// unit-tested without a real binary.
public static class FfmpegVersion
{
    private static readonly Regex Banner = new(@"^\s*ffmpeg version (\S+)");

    /// The version token from ffmpeg's first `-version` line, or null when the
    /// line is missing or isn't the expected banner.
    public static string? Parse(string? firstLine)
    {
        if (firstLine is null) return null;
        var m = Banner.Match(firstLine);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// Runs `ffmpeg -version` and returns the parsed version token, or null if
    /// the binary can't be launched or doesn't print the expected banner. Never
    /// throws: a diagnostics read must not be able to break the caller.
    public static async Task<string?> QueryAsync(string ffmpegPath, CancellationToken ct = default)
    {
        try
        {
            using var p = Process.Start(FfmpegProcess.StartInfo(ffmpegPath, ["-version"]));
            if (p is null) return null;
            // Drain both pipes so a chatty child can never block on a full buffer.
            var stdout = p.StandardOutput.ReadToEndAsync(ct);
            var stderr = p.StandardError.ReadToEndAsync(ct);
            var text = await stdout;
            await stderr;
            await p.WaitForExitAsync(ct);
            return Parse(text.Split('\n', 2)[0].TrimEnd('\r'));
        }
        catch
        {
            return null;
        }
    }
}
