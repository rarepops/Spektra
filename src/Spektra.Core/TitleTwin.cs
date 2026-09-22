namespace Spektra.Core;

/// Does a file that matched nothing by audio still have a same-titled
/// counterpart under another scan root?
///
/// This exists for the folder diff ONLY, and the distinction is the whole
/// design. Grouping decides what a user deletes, so it stays what
/// DuplicateGrouper says it is: audio and nothing else, because a name is not
/// evidence worth a file. A diff answers a different question, "do I have this
/// track on both sides", where a wrong yes costs a second look and a wrong no
/// costs a hunt through hundreds of files that were never missing. Names are
/// good evidence for that question, so the diff may use what the grouper must
/// not, and the two verdicts are reported separately rather than merged.
///
/// Measured on the owner's library (2026-09-09): of 319 files the diff called
/// "only in this folder", 278 had a same-titled counterpart on the other side
/// and 41 were genuinely absent.
public static class TitleTwin
{
    /// A subset must still share this many words. Containment on its own would
    /// twin "Portishead - Roads" with "Portishead - Roads To Nowhere At All",
    /// and any track whose name happens to contain a short one.
    private const int MinSharedWords = 3;

    /// Words that appear on one side of a library and not the other without
    /// ever naming a different track: release-format and version noise, plus
    /// the articles a tagger drops at will. Stripping them costs a little
    /// fidelity ("a-ha" keeps only "ha") and that is harmless, because both
    /// sides are stripped by the same rule and only their agreement is read.
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "remaster", "remastered", "radio", "edit", "original", "mix", "extended",
        "instrumental", "album", "single", "version", "explicit", "clean", "mono",
        "stereo", "remix", "feat", "ft", "the", "a", "an", "and",
    };

    /// The words a file name offers as evidence: lower-cased, split on
    /// everything that is not a letter or digit, noise and years dropped, and
    /// deduplicated, so "Duran Duran" cannot outweigh the rest of a name.
    public static IReadOnlySet<string> Words(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var set = new HashSet<string>(StringComparer.Ordinal);
        var word = new System.Text.StringBuilder();
        foreach (var c in name.Replace("&", " and "))
        {
            if (char.IsLetterOrDigit(c)) word.Append(c);
            else { Add(set, word); word.Clear(); }
        }
        Add(set, word);
        return set;
    }

    private static void Add(HashSet<string> set, System.Text.StringBuilder word)
    {
        if (word.Length == 0) return;
        var w = word.ToString();
        if (Noise.Contains(w)) return;
        // A bare year is a reissue's stamp, never the difference between two
        // tracks; it arrives attached to the noise words already dropped.
        if (w.Length == 4 && int.TryParse(w, out var year) && year is >= 1900 and <= 2099) return;
        set.Add(w);
    }

    /// True when two names are evidence of the same track: the same words, or
    /// one name's words wholly inside the other's with enough of them to mean
    /// something.
    ///
    /// Containment, deliberately, and NOT symmetric similarity. The commonest
    /// way two libraries disagree about a name is that one credits every
    /// featured artist and the other only the headline act, which leaves the
    /// short name a strict subset of the long one. Counting the extra artists
    /// against the match rejects most of those: on the owner's library a
    /// Jaccard bar high enough to keep "Alexei Scutari - Dacia" apart from
    /// "Alexei Scutari - Spirit" also threw out "Thomas Bergersen; Two Steps
    /// from Hell - Heart of Courage" against "Thomas Bergersen - Heart of
    /// Courage", which is the same recording. Containment separates those two
    /// cases exactly, because a different track by the same artist is never a
    /// subset: it brings its own title words.
    public static bool SameTrack(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return false;
        if (a.Count == b.Count && a.SetEquals(b)) return true;
        var shared = 0;
        var (small, large) = a.Count <= b.Count ? (a, b) : (b, a);
        foreach (var w in small) if (large.Contains(w)) shared++;
        return shared == small.Count && shared >= MinSharedWords;
    }

    /// For each needle, the path of a file under a DIFFERENT root that names
    /// the same track, or no entry at all when there is none.
    ///
    /// The haystack is every scanned file, not only the other unpaired ones: a
    /// track can be grouped on one side (two copies over there that matched
    /// each other) while this side's copy matched nothing, and "is it over
    /// there at all" is still yes. Same-root twins are skipped because two
    /// copies in one folder are a duplicate, which is the other view's
    /// business, not a difference between folders.
    public static IReadOnlyDictionary<string, string> FindAcrossRoots(
        IEnumerable<(string Path, string Root)> needles,
        IEnumerable<(string Path, string Root)> haystack)
    {
        var hay = haystack.Select(h => (h.Path, h.Root, Words: Words(h.Path))).ToArray();

        // Every match shares at least one word (a subset shares all of the
        // smaller set), so the postings of a needle's own words are a complete
        // candidate list, and the alternative is comparing every file with
        // every other one.
        var byWord = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < hay.Length; i++)
            foreach (var w in hay[i].Words)
            {
                if (!byWord.TryGetValue(w, out var list)) byWord[w] = list = [];
                list.Add(i);
            }

        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<int>();
        foreach (var (path, root) in needles)
        {
            var words = Words(path);
            seen.Clear();
            (string Path, bool Exact, int Length)? best = null;
            foreach (var w in words)
            {
                if (!byWord.TryGetValue(w, out var postings)) continue;
                foreach (var i in postings)
                {
                    if (!seen.Add(i)) continue;
                    var cand = hay[i];
                    if (string.Equals(cand.Root, root, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(cand.Path, path, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!SameTrack(words, cand.Words)) continue;

                    // Closest name wins, so the answer does not depend on
                    // directory order: an exact word set beats a subset, then
                    // the shorter name, then the path, which settles ties for
                    // good rather than leaving them to the file system.
                    var exact = words.Count == cand.Words.Count && words.SetEquals(cand.Words);
                    var length = System.IO.Path.GetFileName(cand.Path).Length;
                    var better = best is not { } b
                        || (exact != b.Exact ? exact
                            : length != b.Length ? length < b.Length
                            : string.CompareOrdinal(cand.Path, b.Path) < 0);
                    if (better) best = (cand.Path, exact, length);
                }
            }
            if (best is { } winner) found[path] = winner.Path;
        }
        return found;
    }
}
