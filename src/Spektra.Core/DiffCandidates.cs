namespace Spektra.Core;

/// Which of the folders already open are worth offering as the other side of a
/// folder diff, for the Analyze menu's "Compare 'X' with" submenu.
///
/// Small, but the rules are real: a folder cannot be compared with itself, and
/// two tabs can reach one folder by different spellings or by one drilling down
/// to where another is rooted. Offering such an entry would promise a
/// comparison the window then cannot make, because SetDiffRoots collapses the
/// pair to a single root and draws one column.
public static class DiffCandidates
{
    public static IReadOnlyList<string> Other(string current, IEnumerable<string> openScopes) =>
        [.. openScopes
            .Where(s => !string.Equals(s, current, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}
