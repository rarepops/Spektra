namespace Spektra.Core;

/// What a saved session resolves to once vanished files are dropped.
public sealed record RestoredSession(IReadOnlyList<string> Paths, string? Selected);

/// Turns a saved tab list into the tabs actually worth reopening. Pure: the
/// caller supplies the existence test, so this is testable without touching
/// the disk.
public static class SessionRestore
{
    /// Drops paths that no longer exist. The selection is resolved by PATH
    /// rather than by position, because dropping an earlier entry shifts
    /// every later index; a selection whose file vanished falls back to the
    /// first survivor. An out-of-range index is treated as no selection.
    public static RestoredSession Resolve(
        IReadOnlyList<string>? savedPaths, int selectedIndex, Func<string, bool> exists)
    {
        if (savedPaths is null || savedPaths.Count == 0)
            return new RestoredSession([], null);

        var wanted = selectedIndex >= 0 && selectedIndex < savedPaths.Count
            ? savedPaths[selectedIndex]
            : null;

        var survivors = savedPaths.Where(exists).ToList();
        if (survivors.Count == 0)
            return new RestoredSession([], null);

        var selected = wanted is not null && survivors.Contains(wanted, StringComparer.OrdinalIgnoreCase)
            ? wanted
            : survivors[0];

        return new RestoredSession(survivors, selected);
    }
}
