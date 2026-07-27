using System.Text.RegularExpressions;
using Spektra.Core;

namespace Spektra.Tests;

/// Guards a duplication the installer only enforces by comment: the shell-verb
/// extension list in packaging\spektra.wxs (AudioExts) has to match
/// BandwidthReport.AudioExtensions, or the installer silently stops offering
/// "Analyze with Spektra" for a codec the audit pipeline already understands.
public class InstallerAudioExtensionsSyncTests
{
    private static readonly string WxsPath =
        Path.Combine(AppContext.BaseDirectory, "packaging", "spektra.wxs");

    // Whitespace-tolerant and unanchored to a line number, so a reformatted or
    // reordered .wxs still resolves; RegexOptions.Singleline lets `.` reach
    // across a stray line break inside the define without needing one.
    private static readonly Regex AudioExtsDefine = new(
        @"<\?define\s+AudioExts\s*=\s*(?<list>[^?]*?)\s*\?>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    [Test]
    public async Task WxsAudioExts_MatchesBandwidthReportAudioExtensions()
    {
        var wxsExtensions = ParseWxsAudioExtensions();
        var coreExtensions = BandwidthReport.AudioExtensions;

        var onlyInWxs = wxsExtensions.Except(coreExtensions).ToArray();
        var onlyInCore = coreExtensions.Except(wxsExtensions).ToArray();

        await Assert.That(wxsExtensions.Length).IsEqualTo(coreExtensions.Length)
            .Because(
                $"packaging\\spektra.wxs AudioExts has {wxsExtensions.Length} extension(s), " +
                $"BandwidthReport.AudioExtensions has {coreExtensions.Length}. " +
                $"Only in .wxs: [{string.Join(", ", onlyInWxs)}]. " +
                $"Only in Core: [{string.Join(", ", onlyInCore)}]. " +
                "Someone edited one list without the other; keep them in sync.");

        await Assert.That(wxsExtensions.SequenceEqual(coreExtensions)).IsTrue()
            .Because(
                "packaging\\spektra.wxs AudioExts and BandwidthReport.AudioExtensions have the " +
                "same count but disagree on membership or order. " +
                $".wxs: [{string.Join(", ", wxsExtensions)}]. " +
                $"Core: [{string.Join(", ", coreExtensions)}]. " +
                $"Only in .wxs: [{string.Join(", ", onlyInWxs)}]. " +
                $"Only in Core: [{string.Join(", ", onlyInCore)}].");
    }

    private static string[] ParseWxsAudioExtensions()
    {
        if (!File.Exists(WxsPath))
            throw new FileNotFoundException(
                $"Expected the packaged installer source at '{WxsPath}' (copied from " +
                "packaging\\spektra.wxs via the test project's None include). If this test " +
                "project stopped copying it, the guard can't run.", WxsPath);

        var wxs = File.ReadAllText(WxsPath);
        var match = AudioExtsDefine.Match(wxs);

        // A guard that passes when it can't find its input is worse than no
        // guard: fail loudly instead of quietly comparing against [].
        if (!match.Success)
            throw new InvalidOperationException(
                $"Could not find '<?define AudioExts=...?>' in '{WxsPath}'. The installer's " +
                "audio-extension list may have been renamed or restructured; update this " +
                "test's parser to match.");

        return match.Groups["list"].Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }
}
