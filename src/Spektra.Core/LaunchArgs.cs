namespace Spektra.Core;

/// One `--compare a b` request, with its optional view flags.
public sealed record ComparePair(string PathA, string PathB, bool AutoAlign, string? Mode);

/// One `--diff a b` request: two folders to open as a folder diff. Two roots
/// rather than one is the whole point, since a diff draws a column per root,
/// which is why this cannot ride on --dupes.
public sealed record DiffPair(string FolderA, string FolderB);

/// What a command line asked the GUI to open. Bare means "nothing targeted",
/// which is the only case that restores the previous session's tabs.
public sealed record LaunchRequest(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Folders,
    ComparePair? Compare,
    string? DupesRoot,
    string? ManifestRoot,
    DiffPair? Diff = null)
{
    public bool IsBare =>
        Files.Count == 0 && Folders.Count == 0
        && Compare is null && DupesRoot is null && ManifestRoot is null && Diff is null;
}

/// Parses the GUI's command line. Pure: the two existence predicates are
/// injected so this is testable without a disk, and default to the real ones.
///
/// Unknown flags are ignored on purpose. Explorer verbs are registry rows that
/// outlive the install that wrote them, so a stale row must degrade to a normal
/// launch rather than fail the way the CLI's strict parser does.
public static class LaunchArgs
{
    public static LaunchRequest Parse(
        string[] args,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? dirExists = null)
    {
        var isFile = fileExists ?? File.Exists;
        var isDir = dirExists ?? Directory.Exists;

        ComparePair? compare = null;
        DiffPair? diff = null;
        string? dupesRoot = null;
        string? manifestRoot = null;
        var files = new List<string>();
        var folders = new List<string>();

        // --compare wins the whole line when both paths are real, matching the
        // pre-refactor behavior; a broken pair falls through and its paths are
        // classified like any others.
        if (args is ["--compare", var a, var b, ..] && isFile(a) && isFile(b))
        {
            var mode = ValueAfter(args, "--mode");
            compare = new ComparePair(a, b, args.Contains("--auto"), mode?.ToLowerInvariant());
            return new LaunchRequest([], [], compare, null, null);
        }

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--dupes", StringComparison.Ordinal))
            {
                dupesRoot = TakeFolder(args, ref i, isDir) ?? dupesRoot;
                continue;
            }
            if (string.Equals(arg, "--manifest", StringComparison.Ordinal))
            {
                manifestRoot = TakeFolder(args, ref i, isDir) ?? manifestRoot;
                continue;
            }
            if (string.Equals(arg, "--diff", StringComparison.Ordinal))
            {
                // Both folders or neither. Half a diff is not a diff, and
                // quietly demoting it to a one-root scan would answer a
                // question nobody asked. TakeFolder leaves a "--" candidate
                // alone, so a following switch is still read as a switch
                // rather than swallowed into the second slot.
                var first = TakeFolder(args, ref i, isDir);
                var second = TakeFolder(args, ref i, isDir);
                if (first is not null && second is not null) diff = new DiffPair(first, second);
                continue;
            }
            if (isFile(arg)) files.Add(arg);
            else if (isDir(arg)) folders.Add(arg);
            // Anything else (unknown flags, vanished paths) is dropped.
        }

        return new LaunchRequest(files, folders, compare, dupesRoot, manifestRoot, diff);
    }

    /// Consumes the argument after a switch when it is a real folder. The index
    /// advances either way for a present-but-bad value, so a bogus folder is not
    /// then re-read as a path. A candidate that starts with "--" is left alone
    /// (the index does not advance) so it is re-read as a flag on the next
    /// loop iteration, rather than being swallowed as this switch's value. A
    /// single leading "-" does not count: folder paths never start with "--",
    /// but they (and negative numbers, for other switches) can start with "-".
    private static string? TakeFolder(string[] args, ref int i, Func<string, bool> isDir)
    {
        if (i + 1 >= args.Length) return null;
        var candidate = args[i + 1];
        if (candidate.StartsWith("--", StringComparison.Ordinal)) return null;
        i++;
        return isDir(candidate) ? candidate : null;
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        var at = Array.IndexOf(args, flag);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
