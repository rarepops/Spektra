using System.Text;

namespace Spektra.Core;

/// One row of `spektra-cli dupes --diff` as JSON or CSV. Kind is "suspect" (a
/// same-named pair whose audio differs: A in Root and Path, B in the Other
/// columns), "weak" (a member of a group matched too weakly to take on trust),
/// "only-in" (a file only its own folder holds) or "not-analyzed" (a file the
/// scan could not analyze, with the Reason). NameStrength and Label hold enum
/// names, such as SameName and DoesntLineUp.
public sealed record DupesDiffRow(
    string Kind, string Root, string Path, string? OtherRoot, string? OtherPath,
    string? NameStrength, string? Label, double? AlignConfidence,
    double? Seconds, double? OtherSeconds, string? SameAudioAs, string? OtherSameAudioAs,
    int? Group, string? Reason);

/// What differs between the folders of a dupes scan, as `spektra-cli dupes
/// --diff` prints it: the same-name pairs, the weak matches, what each folder
/// alone holds, and what could not be analyzed, each listed in the order
/// Duplicate Detective lists it. A file in a suspect pair is never also only-in
/// or not-analyzed: the pair already lists it. verdicts[i] is the label of
/// result.Suspects[i]. The roots are taken as given, so they must be spelled as
/// the scan's paths are.
public static class DupesDiffReport
{
    public static IReadOnlyList<DupesDiffRow> Rows(
        DupesResult result, IReadOnlyList<SuspectVerdict> verdicts, IReadOnlyList<string> roots)
    {
        var paired = Paired(result);
        return
        [
            .. Suspects(result, verdicts).Select(s => new DupesDiffRow(
                "suspect", s.Pair.RootA, s.Pair.PathA, s.Pair.RootB, s.Pair.PathB,
                s.Pair.Strength.ToString(), s.Verdict.Label.ToString(),
                s.Verdict.Confidence is { } c ? Math.Round(c, 3) : null,
                s.Pair.SecondsA, s.Pair.SecondsB, s.Pair.SameAudioAsA, s.Pair.SameAudioAsB, null, null)),
            .. WeakGroups(result).SelectMany(g => Members(g).Select(m => new DupesDiffRow(
                "weak", DuplicateScan.RootOf(m.Path, roots), m.Path, null, null, null, null, null,
                g.Rows.TryGetValue(m.Path, out var row) ? row.DurationSeconds : null,
                null, null, null, g.Group.Id, null))),
            .. OnlyIn(result, roots, paired).SelectMany(c => c.Files).Select(f => new DupesDiffRow(
                "only-in", f.Root, f.Path, null, null, null, null, null,
                f.Row.DurationSeconds, null, null, null, null, null)),
            .. NotAnalyzed(result, paired).Select(f => new DupesDiffRow(
                "not-analyzed", DuplicateScan.RootOf(f.Path, roots), f.Path, null, null, null, null, null,
                null, null, null, null, null, f.Reason)),
        ];
    }

    public static string Text(
        DupesResult result, IReadOnlyList<SuspectVerdict> verdicts, IReadOnlyList<string> roots)
    {
        var labels = ScopeLabel.Distinguish(roots);
        var paired = Paired(result);
        var suspects = Suspects(result, verdicts);
        var top = suspects.Count(s => SuspectView.IsTopTier(s.Verdict.Label));
        var text = new StringBuilder();

        text.Append(SuspectView.Heading(top, suspects.Count - top, 0, false)).Append('\n');
        var number = 0;
        foreach (var (pair, verdict) in suspects)
        {
            text.Append($"  {++number}. {SuspectView.LabelText(verdict.Label)} · ")
                .Append($"{SuspectView.StrengthText(pair.Strength)} · {SuspectView.Lengths(pair)}\n")
                .Append($"     {SuspectView.LabelOf(pair.RootA, roots, labels)}: {pair.PathA}\n")
                .Append($"     {SuspectView.LabelOf(pair.RootB, roots, labels)}: {pair.PathB}\n");
            if (SuspectView.CrossMatch(pair, roots) is { } cross) text.Append($"     {cross}\n");
        }

        var weak = WeakGroups(result);
        if (weak.Count > 0) text.Append($"Weak matches · {weak.Count}\n");
        foreach (var g in weak)
        {
            text.Append($"  {g.Group.Label} · {g.Group.Members.Count} files · sameness {g.Group.SamenessTier}\n");
            foreach (var m in Members(g)) text.Append($"     {m.Path}\n");
        }

        // Every folder gets its line, as every folder gets its column.
        foreach (var column in OnlyIn(result, roots, paired))
        {
            text.Append(column.Files.Count == 0
                ? $"Only in {column.Label} · nothing\n"
                : $"Only in {column.Label} · {column.Files.Count} file(s) · {Reporting.FormatBytes(column.Bytes)}\n");
            foreach (var f in column.Files) text.Append($"     {f.Path}\n");
        }

        var notAnalyzed = NotAnalyzed(result, paired);
        if (notAnalyzed.Count > 0) text.Append($"{notAnalyzed.Count} file(s) not analyzed\n");
        foreach (var f in notAnalyzed) text.Append($"     {f.Path} ({f.Reason})\n");
        return text.ToString();
    }

    /// Whether anything differs. A file the scan could not analyze says nothing
    /// either way, so rows of those alone do not count.
    public static bool Differs(IReadOnlyList<DupesDiffRow> rows) => rows.Any(r => r.Kind != "not-analyzed");

    // The pairs with their labels, in the order the window lists them.
    private static IReadOnlyList<(SuspectPair Pair, SuspectVerdict Verdict)> Suspects(
        DupesResult result, IReadOnlyList<SuspectVerdict> verdicts) =>
        SuspectView.Visible(
            result.Suspects.Select((p, i) => (Pair: p, Verdict: verdicts[i])), s => s.Pair, s => s.Verdict, []);

    // Every file a suspect pair names.
    private static HashSet<string> Paired(DupesResult result) =>
        new(result.Suspects.SelectMany(p => new[] { p.PathA, p.PathB }), StringComparer.OrdinalIgnoreCase);

    // The groups matched too weakly to take on trust, in the diff's order.
    private static IReadOnlyList<DupesGroupReport> WeakGroups(DupesResult result) =>
        FolderDiffView.VisibleGroups(result.Groups, g => g, [], onlyDifferences: true);

    private static IEnumerable<DuplicateMember> Members(DupesGroupReport group) =>
        group.Group.Members.OrderBy(m => m.Path, StringComparer.OrdinalIgnoreCase);

    // The diff's columns, one per root in root order, over the files no pair names.
    private static IReadOnlyList<DiffColumn> OnlyIn(
        DupesResult result, IReadOnlyList<string> roots, HashSet<string> paired) =>
        FolderDiffView.Columns(
            [.. result.Unpaired.Where(f => !paired.Contains(f.Path))], roots, [], hideTitleTwins: false);

    private static List<NotAnalyzedFile> NotAnalyzed(DupesResult result, HashSet<string> paired) =>
        [.. result.NotAnalyzed.Where(f => !paired.Contains(f.Path)).OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)];
}
