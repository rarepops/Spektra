namespace Spektra.Core;

/// How closely two names that already read as the same track agree. SameName: the same words with
/// the version words kept, or the same title with one artist list inside the other. NearName: the
/// same words only once the version words are dropped, or any other containment.
public enum NameStrength { SameName, NearName }

/// One collected file as the suspect rule sees it: where it is, which audio group holds it (null when
/// none: unpaired, or never analyzed), and its decoded length when the scan learned it.
public sealed record ScannedFile(string Path, string Root, int? Group, double? Seconds);

/// Two files under different roots whose names say they are the same track while their audio groups
/// disagree. A is the file under the root listed first. SameAudioAsX is another member of X's audio
/// group, preferring one under a different root than X's, or null when X is in no group.
public sealed record SuspectPair(
    string PathA, string RootA, double? SecondsA, string? SameAudioAsA,
    string PathB, string RootB, double? SecondsB, string? SameAudioAsB,
    NameStrength Strength);

public static class SuspectPairs
{
    /// Every pair of files under different roots whose names say the same track while their audio
    /// groups disagree. The file under the root listed first is the left one, and the pairs are
    /// sorted by the left path, then the right.
    ///
    /// A file is a suspect with respect to another root when none of its closest-named twins under
    /// that root shares its audio group. Closest means the lowest `TitleTwin.NameRank` present
    /// there: identical names, then the same title with other artist credits, then near names. Any
    /// shared group counts, High or Medium alike, and two files in no group never share one. The
    /// twin shown is the one in the same folder below its root first, then `TitleTwin`'s
    /// closest-twin order: the same words, the shorter file name, the path.
    public static IReadOnlyList<SuspectPair> Find(IReadOnlyList<ScannedFile> files, IReadOnlyList<string> roots)
    {
        var twins = files.Select(_ => new List<(int Other, int Rank)>()).ToArray();
        foreach (var (a, b) in TitleTwin.CrossRootTwins([.. files.Select(f => (f.Path, f.Root))]))
        {
            var rank = TitleTwin.NameRank(files[a].Path, files[b].Path);
            twins[a].Add((b, rank));
            twins[b].Add((a, rank));
        }

        var found = new Dictionary<(int, int), NameStrength>();
        for (var i = 0; i < files.Count; i++)
            foreach (var underRoot in twins[i].GroupBy(t => files[t.Other].Root, StringComparer.OrdinalIgnoreCase))
            {
                // Only the closest names under a root are asked. A looser twin in the same group
                // as this file must not vouch for it when a better-named twin disagrees.
                var rank = underRoot.Min(t => t.Rank);
                var closest = underRoot.Where(t => t.Rank == rank).Select(t => t.Other).ToList();
                if (files[i].Group is { } g && closest.Any(j => files[j].Group == g)) continue;
                var shown = closest
                    .OrderByDescending(j => SameFolder(files[i], files[j]))
                    .ThenByDescending(j => TitleTwin.Words(files[j].Path).SetEquals(TitleTwin.Words(files[i].Path)))
                    .ThenBy(j => Path.GetFileName(files[j].Path).Length)
                    .ThenBy(j => files[j].Path, StringComparer.Ordinal)
                    .First();
                // Two needles that find the same pair agree on its strength: NameRank is symmetric.
                found.TryAdd((Math.Min(i, shown), Math.Max(i, shown)), rank <= 1 ? NameStrength.SameName : NameStrength.NearName);
            }

        var byGroup = files.Where(f => f.Group is not null).ToLookup(f => f.Group.GetValueOrDefault());
        var pairs = new List<SuspectPair>(found.Count);
        foreach (var ((i, j), strength) in found)
        {
            var (a, b) = RootOrder(roots, files[i].Root) <= RootOrder(roots, files[j].Root)
                ? (files[i], files[j])
                : (files[j], files[i]);
            pairs.Add(new SuspectPair(
                a.Path, a.Root, a.Seconds, SameAudioAs(byGroup, a),
                b.Path, b.Root, b.Seconds, SameAudioAs(byGroup, b),
                strength));
        }
        return [.. pairs
            .OrderBy(p => p.PathA, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.PathB, StringComparer.OrdinalIgnoreCase)];
    }

    /// Whether two files sit in the same folder below their roots, ignoring case.
    private static bool SameFolder(ScannedFile a, ScannedFile b) =>
        string.Equals(FolderBelowRoot(a), FolderBelowRoot(b), StringComparison.OrdinalIgnoreCase);

    private static string FolderBelowRoot(ScannedFile file) =>
        Path.GetDirectoryName(FolderDiffView.RelativePath(file.Path, file.Root)) ?? "";

    /// The place of a root in the list the caller gave; a root it did not list goes last.
    private static int RootOrder(IReadOnlyList<string> roots, string root)
    {
        for (var k = 0; k < roots.Count; k++)
            if (string.Equals(roots[k], root, StringComparison.OrdinalIgnoreCase)) return k;
        return roots.Count;
    }

    /// Another member of the file's audio group, preferring one under a different root, then the
    /// path in ordinal order; null when the file is in no group.
    private static string? SameAudioAs(ILookup<int, ScannedFile> byGroup, ScannedFile file) =>
        file.Group is not { } group ? null
        : byGroup[group]
            .Where(f => !string.Equals(f.Path, file.Path, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => string.Equals(f.Root, file.Root, StringComparison.OrdinalIgnoreCase))
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .Select(f => f.Path)
            .FirstOrDefault();
}
