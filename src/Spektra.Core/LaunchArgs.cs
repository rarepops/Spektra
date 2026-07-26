namespace Spektra.Core;

/// One `--compare a b` request, with its optional view flags.
public sealed record ComparePair(string PathA, string PathB, bool AutoAlign, string? Mode);

/// What a command line asked the GUI to open. Bare means "nothing targeted",
/// which is the only case that restores the previous session's tabs.
public sealed record LaunchRequest(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Folders,
    ComparePair? Compare,
    string? DupesRoot,
    string? ManifestRoot)
{
    public bool IsBare =>
        Files.Count == 0 && Folders.Count == 0
        && Compare is null && DupesRoot is null && ManifestRoot is null;
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
            if (string.Equals(arg, "--dupes", StringComparison.OrdinalIgnoreCase))
            {
                dupesRoot = TakeFolder(args, ref i, isDir) ?? dupesRoot;
                continue;
            }
            if (string.Equals(arg, "--manifest", StringComparison.OrdinalIgnoreCase))
            {
                manifestRoot = TakeFolder(args, ref i, isDir) ?? manifestRoot;
                continue;
            }
            if (isFile(arg)) files.Add(arg);
            else if (isDir(arg)) folders.Add(arg);
            // Anything else (unknown flags, vanished paths) is dropped.
        }

        return new LaunchRequest(files, folders, compare, dupesRoot, manifestRoot);
    }

    /// Consumes the argument after a switch when it is a real folder. The index
    /// advances either way for a present-but-bad value, so a bogus folder is not
    /// then re-read as a path.
    private static string? TakeFolder(string[] args, ref int i, Func<string, bool> isDir)
    {
        if (i + 1 >= args.Length) return null;
        var candidate = args[++i];
        return isDir(candidate) ? candidate : null;
    }

    private static string? ValueAfter(string[] args, string flag)
    {
        var at = Array.IndexOf(args, flag);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }
}
