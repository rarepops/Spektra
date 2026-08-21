namespace Spektra.Core;

/// Reads a set of picked file paths as an intention, for File > Compare.
///
/// The command used to refuse unless two documents were already open, which
/// left its ellipsis lying: every other "…" item in that menu opens a dialog,
/// and every other conditional item dims when it cannot run, so an enabled
/// Compare that quietly wrote to the status bar read as the app ignoring the
/// click. It now opens a picker, and a picker answers with any number of files.
///
/// This lives in Core rather than in the window because Spektra.App has no unit
/// tests by house rule: branching left in a click handler is only ever checked
/// by hand.
public static class ComparePick
{
    public enum Outcome
    {
        /// The picker was dismissed. Not an error, and not worth a message.
        Cancelled,
        /// Exactly two distinct files: go.
        Compare,
        /// One file, so ask for its partner rather than refusing the click.
        NeedSecondFile,
        /// More than two. Taking the first two would compare a pair nobody
        /// named, and the wrong pair is worse than no pair.
        TooMany,
        /// One file twice over. A diff against itself is empty, which looks
        /// like a verdict rather than a mistake.
        SameFileTwice,
    }

    public static Outcome Decide(IReadOnlyList<string> paths) => paths.Count switch
    {
        0 => Outcome.Cancelled,
        1 => Outcome.NeedSecondFile,
        2 => SamePath(paths[0], paths[1]) ? Outcome.SameFileTwice : Outcome.Compare,
        _ => Outcome.TooMany,
    };

    // The GUI ships win-x64 only, where two spellings of a path are one file.
    private static bool SamePath(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
