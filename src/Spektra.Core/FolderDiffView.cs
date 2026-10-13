namespace Spektra.Core;

/// One side of a folder diff: a scan root and the files only it has. Roots
/// with nothing unique still get a column, because "this folder has no extras"
/// and "this folder is not in the comparison" must not look the same.
public sealed record DiffColumn(
    string Root, string Label, IReadOnlyList<UnpairedFile> Files, long Bytes, int AlsoThere)
{
    /// Always names both numbers, and names them even when one list is
    /// hidden. A count of what is left, on its own, cannot be told apart from
    /// a folder that really has nothing extra, and the two mean opposite
    /// things to someone deciding what to copy across.
    public string Summary
    {
        get
        {
            var only = Files.Count(f => f.TwinPath is null);
            var here = only == 0 ? "nothing only here" : $"{only} file(s) only here · {Reporting.FormatBytes(Bytes)}";
            return AlsoThere == 0 ? here : $"{here} · {AlsoThere} the other folder has under a matching name";
        }
    }
}

/// What a folder diff shows for a finished scan, as pure functions of the scan
/// result and the view's switches. Kept in Core so the rules stay test-pinned;
/// the window only copies the answers into its lists.
public static class FolderDiffView
{
    /// The groups a view shows, in the order it shows them.
    ///
    /// The duplicate hunt reads biggest reclaim first, which is the order
    /// the scan hands over and the question that view asks. A diff reclaims
    /// nothing, and its weak matches sit under two alphabetical columns,
    /// where reclaim order reads as no order at all.
    public static IReadOnlyList<T> VisibleGroups<T>(
        IReadOnlyList<T> groups, Func<T, DupesGroupReport> reportOf,
        IReadOnlyCollection<string> tokens, bool onlyDifferences)
    {
        var ordered = onlyDifferences
            ? groups.OrderBy(g => reportOf(g).Group.Label, StringComparer.OrdinalIgnoreCase)
            : (IEnumerable<T>)groups;
        return [.. ordered.Where(g =>
            (tokens.Count == 0 || DuplicateScan.GroupMatches(reportOf(g), tokens))
            && !(onlyDifferences && reportOf(g).IsSameTrack))];
    }

    /// The diff's columns: the files each root alone holds.
    ///
    /// One column per scan root, in the order they were added, including
    /// roots with nothing unique: an empty column says "this folder has
    /// no extras", which is an answer, not an absence.
    public static IReadOnlyList<DiffColumn> Columns(
        IReadOnlyList<UnpairedFile> unpaired, IReadOnlyList<string> roots,
        IReadOnlyCollection<string> tokens, bool hideTitleTwins)
    {
        var byRoot = unpaired
            .Where(u => tokens.Count == 0 || Matches(u, tokens))
            .GroupBy(u => u.Root, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        // Computed over all the roots at once: a column's label is only
        // meaningful next to its neighbours, so it cannot be worked out
        // one root at a time inside DiffColumn.
        var labels = ScopeLabel.Distinguish(roots);
        var columns = new List<DiffColumn>(roots.Count);
        for (var i = 0; i < roots.Count; i++)
        {
            var files = byRoot.GetValueOrDefault(roots[i]) ?? [];
            var alsoThere = files.Count(f => f.TwinPath is not null);
            var shown = hideTitleTwins ? files.Where(f => f.TwinPath is null).ToList() : files;
            columns.Add(new DiffColumn(
                roots[i],
                labels[i],
                // Files the other folder has not got come first even when
                // both kinds are shown: they are the answer, the rest is
                // the working.
                [.. shown.OrderBy(f => f.TwinPath is not null)
                         .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)],
                shown.Sum(f => f.SizeBytes),
                alsoThere));
        }
        return columns;
    }

    /// What the footer adds to a scan's base line for the current view: empty
    /// in the ordinary view with no filter typed.
    public static string FooterSuffix(
        bool onlyDifferences, bool filtered, int sameHidden, int nameMatched, int inOneFolderOnly,
        int visibleGroups, int totalGroups, int notComparable)
    {
        var suffix = "";
        if (onlyDifferences)
        {
            // Say what was hidden, not only what is left. An empty diff is a
            // real answer ("these folders hold the same music") and without a
            // count it is indistinguishable from a scan that found nothing.
            // Confidence, descending: proved the same, named the same, only
            // one side has it, too weak to call either way.
            suffix += $" · differences: {sameHidden} same hidden";
            if (nameMatched > 0) suffix += $" · {nameMatched} same name only";
            suffix += $" · {inOneFolderOnly} in one folder only · {visibleGroups} weak match";
            // Never let an unanalysable file pass as agreement: a file absent
            // from a diff reads as "these folders match", which would be a lie.
            if (notComparable > 0)
                suffix += $" · {notComparable} not comparable";
        }
        if (filtered)
            suffix += $" · filter: {visibleGroups} of {totalGroups} groups";
        return suffix;
    }

    /// What to say beside a file the other folder has under a matching name,
    /// or null when it has no such file.
    public static string? TwinNote(UnpairedFile file) =>
        file.TwinPath is not { } twin ? null
        : string.Equals(Path.GetFileName(twin), Path.GetFileName(file.Path), StringComparison.OrdinalIgnoreCase)
            ? "the other folder has this name too · the audio differs"
            : $"the other folder has: {Path.GetFileName(twin)}";

    /// The file's path below its root, which is what a column shows: the
    /// column's heading already names the root.
    public static string RelativePath(UnpairedFile file) =>
        file.Root.Length > 0 && file.Path.StartsWith(file.Root, StringComparison.OrdinalIgnoreCase)
            ? file.Path[file.Root.Length..].TrimStart('\\', '/')
            : file.Path;

    /// The results filter applied to an unpaired file: every word must appear
    /// in its path, matching how GroupMatches treats a group's members.
    private static bool Matches(UnpairedFile file, IReadOnlyCollection<string> tokens) =>
        tokens.All(t => file.Path.Contains(t, StringComparison.OrdinalIgnoreCase));
}
