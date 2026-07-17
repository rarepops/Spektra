using System.Text.RegularExpressions;

namespace Spektra.Core;

public sealed record DuplicateMember(string Path, double Sameness, string SamenessTier, bool FoundByAudio);

public sealed record DuplicateGroup(int Id, string Label, string SamenessTier, IReadOnlyList<DuplicateMember> Members);

/// Union-find over audio-confirmed pairs. Names and tags never group anything:
/// they label the group and mark renamed strays ("found by audio"). The group
/// headline wears the WEAKEST member's tier, so a transitive chain cannot
/// masquerade as certainty.
public static partial class DuplicateGrouper
{
    public sealed record FileIdentity(string Path, string? Artist, string? Title);

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)")]
    private static partial Regex BracketedSegments();

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex Separators();

    public static IReadOnlyList<DuplicateGroup> Group(
        IReadOnlyList<FileIdentity> files,
        IReadOnlyList<(int A, int B, FingerprintMatcher.MatchResult Match)> pairs)
    {
        // Admission gate, calibrated on the first real-library run
        // (2026-07-15, 402 files): different songs routinely align a shared
        // section (an intro, a beat, a fade) at high similarity over a small
        // fraction of the track, and those links chained dozens of tracks
        // into one blob. Full-length overlap is what separates a twin from a
        // coincidence, so it gates admission; similarity alone sets the tier.
        var admitted = pairs
            .Where(p => p.Match.OverlapFraction >= FingerprintMatcher.FullConfidenceOverlap)
            .ToList();

        var parent = Enumerable.Range(0, files.Count).ToArray();
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        foreach (var (a, b, _) in admitted)
            parent[Find(a)] = Find(b);

        var best = new Dictionary<int, (double Sim, string Tier)>();
        foreach (var (a, b, m) in admitted)
        {
            var tier = TierOf(m);
            foreach (var i in (ReadOnlySpan<int>)[a, b])
                if (!best.TryGetValue(i, out var cur) || m.Similarity > cur.Sim)
                    best[i] = (m.Similarity, tier);
        }

        var components = new Dictionary<int, List<int>>();
        foreach (var i in best.Keys)
        {
            var root = Find(i);
            if (!components.TryGetValue(root, out var list))
                components[root] = list = [];
            list.Add(i);
        }

        var groups = new List<DuplicateGroup>();
        var id = 1;
        foreach (var indices in components.Values.Where(c => c.Count >= 2))
        {
            var label = LabelFor(indices.Select(i => files[i]).ToList());
            var labelTokens = Separators().Split(label.ToLowerInvariant()).Where(t => t.Length > 0).ToHashSet();
            var members = indices
                .OrderBy(i => files[i].Path, StringComparer.OrdinalIgnoreCase)
                .Select(i =>
                {
                    var identity = files[i];
                    var artist = identity.Artist;
                    return new DuplicateMember(
                        identity.Path, best[i].Sim, best[i].Tier,
                        FoundByAudio: !NameTokens(identity.Path).Any(labelTokens.Contains)
                            && (artist is null || !labelTokens.Contains(artist.ToLowerInvariant())));
                })
                .ToList();
            var groupTier = members.All(m => m.SamenessTier == "High") ? "High" : "Medium";
            groups.Add(new DuplicateGroup(id++, label, groupTier, members));
        }
        return groups;
    }

    internal static string TierOf(FingerprintMatcher.MatchResult m) =>
        m.Similarity >= FingerprintMatcher.HighThreshold
            && m.OverlapFraction >= FingerprintMatcher.FullConfidenceOverlap
        ? "High"
        : "Medium";

    /// Consensus "Artist - Title" from tags when at least two members agree
    /// (or one member is the only tagged one); otherwise the tokens every
    /// member's filename shares; otherwise the first member's tokens.
    private static string LabelFor(IReadOnlyList<FileIdentity> members)
    {
        var tagPair = members
            .Where(f => !string.IsNullOrWhiteSpace(f.Artist) && !string.IsNullOrWhiteSpace(f.Title))
            .GroupBy(f => (Artist: f.Artist!.Trim(), Title: f.Title!.Trim()))
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        if (tagPair is not null)
            return $"{tagPair.Key.Artist} - {tagPair.Key.Title}";

        var common = members
            .Select(f => NameTokens(f.Path).ToHashSet())
            .Aggregate((acc, next) => { acc.IntersectWith(next); return acc; });
        var source = common.Count > 0 ? common : NameTokens(members[0].Path).ToHashSet();
        // Preserve the first member's token order for readability.
        var ordered = NameTokens(members[0].Path).Where(source.Contains).ToArray();
        return ordered.Length > 0 ? string.Join(" ", ordered) : Path.GetFileNameWithoutExtension(members[0].Path);
    }

    /// Lowercased word tokens of the file name: extension gone, bracketed and
    /// parenthesized segments gone, digit-only tokens (track numbers, years)
    /// gone.
    public static string[] NameTokens(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        name = BracketedSegments().Replace(name, " ");
        return [.. Separators().Split(name.ToLowerInvariant())
            .Where(t => t.Length > 0 && !t.All(char.IsDigit))];
    }
}
