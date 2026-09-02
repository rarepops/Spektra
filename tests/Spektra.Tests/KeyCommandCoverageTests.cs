using Spektra.Core;

namespace Spektra.Tests;

/// Guards the one seam the key map cannot close by itself. A KeyCommand is
/// defined in Core, given a default, documented in the README, and listed in
/// the Controls window, all without the shell ever being asked to do anything
/// about it. Add a command and forget the dispatch and every one of those
/// surfaces will confidently describe a key that does nothing.
///
/// Reads the shell's source as text, the way InstallerAudioExtensionsSyncTests
/// reads the .wxs, because the test project links Spektra.Core only.
public sealed class KeyCommandCoverageTests
{
    private static readonly string InputPath =
        Path.Combine(AppContext.BaseDirectory, "app", "MainWindow.Input.cs");

    [Test]
    public async Task Every_command_is_wired_to_something_in_the_shell()
    {
        var source = ReadDispatch();

        foreach (var command in Enum.GetValues<KeyCommand>())
            await Assert.That(source.Contains($"KeyCommand.{command}", StringComparison.Ordinal))
                .IsTrue()
                .Because($"KeyCommand.{command} is bound and documented but never handled in " +
                         "MainWindow.Input.cs, so pressing its key would do nothing.");
    }

    /// Deliberately a substring check rather than a parse: commands are handled
    /// both as `case KeyCommand.X:` and folded into an `or` pattern with a
    /// sibling, and a regex tight enough to tell those apart would break on the
    /// next reasonable refactor. It cannot prove a command is wired to the
    /// RIGHT thing; it proves nobody forgot it entirely, which is the mistake
    /// this actually catches.
    private static string ReadDispatch()
    {
        // A guard that passes when it cannot find its input is worse than no
        // guard, so this throws rather than comparing against an empty string.
        if (!File.Exists(InputPath))
            throw new FileNotFoundException(
                $"Expected the shell's key dispatch at '{InputPath}' (copied from " +
                "src\\Spektra.App\\MainWindow.Input.cs by the test project's None include). " +
                "If that copy stopped happening, this guard cannot run.", InputPath);

        return File.ReadAllText(InputPath);
    }
}
