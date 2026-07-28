using System.Text.RegularExpressions;

namespace Spektra.Tests;

/// Guards the one character that broke Explorer integration in 0.17.0.
///
/// The file verb shipped as `"spektra.exe" %*`. In a shell verb command, %*
/// expands to the parameters that follow the file, not to the file itself, so it
/// arrived empty: every "Analyze with Spektra" click launched the app with a bare
/// command line and it restored the previous session instead of opening the file.
/// Measured on Windows 11 against both templates, single and multiple selections.
///
/// Nothing else in the build could catch this. The rows are correct XML, the MSI
/// compiles, the feature installs, and the app starts.
public class InstallerShellVerbTests
{
    private static readonly string WxsPath =
        Path.Combine(AppContext.BaseDirectory, "packaging", "spektra.wxs");

    // The file-verb command row, whichever extension the ?foreach is on.
    private static readonly Regex FileVerbCommand = new(
        @"<RegistryValue\s+Key=""command""\s+Type=""string""\s+Value=""(?<cmd>[^""]*)""",
        RegexOptions.Compiled);

    [Test]
    public async Task NoVerbCommand_UsesStarExpansion()
    {
        // Command rows only. The surrounding comments name %* on purpose, to say
        // why it must not be used.
        foreach (var match in FileVerbCommand.Matches(ReadWxs()))
        {
            var command = ((Match)match).Groups["cmd"].Value;

            await Assert.That(command.Contains("%*", StringComparison.Ordinal)).IsFalse()
                .Because(
                    $"The verb command [{command}] uses %*, which expands to the arguments AFTER " +
                    "the file, so it arrives empty: the app is launched with no command line and " +
                    "silently restores the previous session instead of opening what was clicked. " +
                    "This shipped in 0.17.0. Pass the file with \"%1\" instead.");
        }
    }

    [Test]
    public async Task EveryVerbCommandRow_SubstitutesAPath()
    {
        var wxs = ReadWxs();
        var matches = FileVerbCommand.Matches(wxs);

        // A guard that finds nothing is not a guard.
        await Assert.That(matches.Count).IsGreaterThan(0)
            .Because(
                $"Found no <RegistryValue Key=\"command\" ...> rows in '{WxsPath}'. The verb rows " +
                "were renamed or restructured; update this test's parser to match.");

        foreach (var match in matches)
        {
            var command = ((Match)match).Groups["cmd"].Value;
            var substitutes =
                command.Contains("%1", StringComparison.Ordinal) ||
                command.Contains("%V", StringComparison.Ordinal);

            await Assert.That(substitutes).IsTrue()
                .Because(
                    $"The verb command [{command}] names no file or folder to act on. A command " +
                    "row has to substitute %1 (file, or folder under Directory\\shell) or %V " +
                    "(folder under Directory\\Background\\shell), or the app is launched with " +
                    "nothing and looks like it ignored the click.");
        }
    }

    [Test]
    public async Task SubstitutionsAreQuoted_SoPathsWithSpacesSurvive()
    {
        var wxs = ReadWxs();

        foreach (var match in FileVerbCommand.Matches(wxs))
        {
            var command = ((Match)match).Groups["cmd"].Value;

            // The .wxs holds quotes as &quot; entities.
            var unquoted =
                Regex.IsMatch(command, @"(?<!&quot;)%1") ||
                Regex.IsMatch(command, @"(?<!&quot;)%V");

            await Assert.That(unquoted).IsFalse()
                .Because(
                    $"The verb command [{command}] substitutes a path without wrapping it in " +
                    "&quot;...&quot;. Music paths contain spaces constantly; unquoted, the shell " +
                    "splits one path into several arguments, none of which exist, and the parser " +
                    "drops all of them.");
        }
    }

    private static string ReadWxs()
    {
        if (!File.Exists(WxsPath))
            throw new FileNotFoundException(
                $"Expected the packaged installer source at '{WxsPath}' (copied from " +
                "packaging\\spektra.wxs via the test project's None include). If this test " +
                "project stopped copying it, the guard can't run.", WxsPath);

        return File.ReadAllText(WxsPath);
    }
}
