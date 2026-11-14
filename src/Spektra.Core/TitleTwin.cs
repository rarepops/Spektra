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

    /// The release-format and version words. `Words` drops them, because they
    /// never decide whether a track is on both sides; `VersionedWords` keeps
    /// them, because they do decide whether two copies are one recording.
    private static readonly HashSet<string> VersionWords = new(StringComparer.Ordinal)
    {
        "remaster", "remastered", "radio", "edit", "original", "mix", "extended",
        "instrumental", "album", "single", "version", "explicit", "clean", "mono",
        "stereo", "remix",
    };

    /// The credit markers and the articles a tagger drops at will. Neither says
    /// anything about the recording, so both readings of a name drop them.
    private static readonly HashSet<string> CreditsAndArticles = new(StringComparer.Ordinal)
    {
        "feat", "ft", "the", "a", "an", "and",
    };

    /// Words that appear on one side of a library and not the other without
    /// ever naming a different track: release-format and version noise, plus
    /// the articles a tagger drops at will. Stripping them costs a little
    /// fidelity ("a-ha" keeps only "ha") and that is harmless, because both
    /// sides are stripped by the same rule and only their agreement is read.
    /// It is the two lists above together, so `Words` drops what it always did.
    private static readonly HashSet<string> Noise = new(VersionWords.Concat(CreditsAndArticles), StringComparer.Ordinal);

    /// The words a file name offers as evidence: lower-cased, split on
    /// everything that is not a letter or digit, noise and years dropped, and
    /// deduplicated, so "Duran Duran" cannot outweigh the rest of a name.
    public static IReadOnlySet<string> Words(string path) =>
        WordsOf(System.IO.Path.GetFileNameWithoutExtension(path), Noise);

    /// `Words` with the version words kept: only the credits, the articles and
    /// the years are dropped. `Words` drops the version words on purpose, which
    /// is right for "does the other folder have this track" and wrong for "is
    /// this the same recording": on its own it twins "Song - Radio Edit" with
    /// "Song - Extended Mix". This reading keeps what tells those two apart.
    public static IReadOnlySet<string> VersionedWords(string path) =>
        WordsOf(System.IO.Path.GetFileNameWithoutExtension(path), CreditsAndArticles);

    /// The one reading both of the above share: they differ only in `drop`.
    private static IReadOnlySet<string> WordsOf(string text, IReadOnlySet<string> drop)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var word = new System.Text.StringBuilder();
        foreach (var c in text.ToLowerInvariant().Replace("&", " and "))
        {
            if (char.IsLetterOrDigit(c)) word.Append(c);
            else { Add(set, word, drop); word.Clear(); }
        }
        Add(set, word, drop);
        return set;
    }

    private static void Add(HashSet<string> set, System.Text.StringBuilder word, IReadOnlySet<string> drop)
    {
        if (word.Length == 0) return;
        var w = word.ToString();
        if (drop.Contains(w)) return;
        // A bare year is a reissue's stamp, never the difference between two
        // tracks, so both readings drop it.
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

    /// How closely two names agree, for two names `SameTrack` already accepted:
    /// 0 when they hold the same words with the version words kept, 1 when they
    /// have the same title and one artist list is inside the other, 2 for
    /// anything else.
    ///
    /// This exists because `Words` drops the version words on purpose, which is
    /// right for "does the other folder have this track" and wrong for "is this
    /// the same recording": on its own it twins "Song - Radio Edit" with "Song -
    /// Extended Mix". The rank puts that difference back without touching the
    /// relation. It only orders pairs `SameTrack` accepted and never adds a
    /// twin it refused, so the rank of any other pair means nothing.
    ///
    /// The artists are the words before the first " - ", read like `Words`; the
    /// title is the rest, read like `VersionedWords`. A name without a " - " (an
    /// en dash does not count) has no artists and is all title, so two of them
    /// rank 0 or 2. A title with nothing left after the credits and articles
    /// does not count as the same title, so it never earns a 1.
    public static int NameRank(string pathA, string pathB)
    {
        if (VersionedWords(pathA).SetEquals(VersionedWords(pathB))) return 0;
        var (artistsA, titleA) = ArtistsAndTitle(pathA);
        var (artistsB, titleB) = ArtistsAndTitle(pathB);
        return titleA.Count > 0 && titleA.SetEquals(titleB)
            && (artistsA.IsSubsetOf(artistsB) || artistsB.IsSubsetOf(artistsA)) ? 1 : 2;
    }

    private static (IReadOnlySet<string> Artists, IReadOnlySet<string> Title) ArtistsAndTitle(string path)
    {
        const string separator = " - ";
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var split = name.IndexOf(separator, StringComparison.Ordinal);
        return split < 0
            ? (WordsOf("", Noise), WordsOf(name, CreditsAndArticles))
            : (WordsOf(name[..split], Noise), WordsOf(name[(split + separator.Length)..], CreditsAndArticles));
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
        var hay = haystack.Select(h => new Named(h.Path, h.Root, Words(h.Path))).ToArray();
        var byWord = IndexByWord(hay);

        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<int>();
        foreach (var (path, root) in needles)
            if (ClosestTwin(new Named(path, root, Words(path)), hay, byWord, seen) is { } twin)
                found[path] = twin;
        return found;
    }

    /// Every pair of files under different roots whose names are twins, as
    /// indexes into `files`: each pair once, the lower index first, in index
    /// order. It is the relation `FindAcrossRoots` reads, all of it, where that
    /// keeps only the closest twin of each needle, so any file it names a twin
    /// for is also in a pair here.
    ///
    /// The same rules apply: a twin under the same root is a duplicate, not a
    /// difference between folders, and one file listed under two roots is not
    /// its own twin.
    public static IReadOnlyList<(int A, int B)> CrossRootTwins(IReadOnlyList<(string Path, string Root)> files)
    {
        var hay = files.Select(f => new Named(f.Path, f.Root, Words(f.Path))).ToArray();
        var byWord = IndexByWord(hay);

        var pairs = new List<(int A, int B)>();
        var seen = new HashSet<int>();
        for (var a = 0; a < hay.Length; a++)
        {
            seen.Clear();
            foreach (var w in hay[a].Words)
                foreach (var b in byWord[w])
                {
                    if (b <= a || !seen.Add(b)) continue;
                    if (string.Equals(hay[b].Root, hay[a].Root, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(hay[b].Path, hay[a].Path, StringComparison.OrdinalIgnoreCase)) continue;
                    if (SameTrack(hay[a].Words, hay[b].Words)) pairs.Add((a, b));
                }
        }
        // A file's twins turn up word by word, not in index order.
        pairs.Sort();
        return pairs;
    }

    /// A file name reduced to the evidence it offers, kept beside where the
    /// file is.
    private readonly record struct Named(string Path, string Root, IReadOnlySet<string> Words);

    // Every match shares at least one word (a subset shares all of the
    // smaller set), so the postings of a needle's own words are a complete
    // candidate list, and the alternative is comparing every file with
    // every other one.
    private static Dictionary<string, List<int>> IndexByWord(Named[] hay)
    {
        var byWord = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < hay.Length; i++)
            foreach (var w in hay[i].Words)
            {
                if (!byWord.TryGetValue(w, out var list)) byWord[w] = list = [];
                list.Add(i);
            }
        return byWord;
    }

    /// The path of the file under another root whose name is closest to the
    /// needle's, or null when no name there is evidence of the same track.
    /// `seen` is scratch space the caller owns, so one set serves every needle.
    private static string? ClosestTwin(
        Named needle, Named[] hay, Dictionary<string, List<int>> byWord, HashSet<int> seen)
    {
        seen.Clear();
        (string Path, bool Exact, int Length)? best = null;
        foreach (var w in needle.Words)
        {
            if (!byWord.TryGetValue(w, out var postings)) continue;
            foreach (var i in postings)
            {
                if (!seen.Add(i)) continue;
                var cand = hay[i];
                if (string.Equals(cand.Root, needle.Root, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(cand.Path, needle.Path, StringComparison.OrdinalIgnoreCase)) continue;
                if (!SameTrack(needle.Words, cand.Words)) continue;

                var exact = needle.Words.Count == cand.Words.Count && needle.Words.SetEquals(cand.Words);
                var rival = (cand.Path, exact, System.IO.Path.GetFileName(cand.Path).Length);
                if (Closer(rival, best)) best = rival;
            }
        }
        return best?.Path;
    }

    // Closest name wins, so the answer does not depend on
    // directory order: an exact word set beats a subset, then
    // the shorter name, then the path, which settles ties for
    // good rather than leaving them to the file system.
    private static bool Closer(
        (string Path, bool Exact, int Length) rival, (string Path, bool Exact, int Length)? best) =>
        best is not { } b
        || (rival.Exact != b.Exact ? rival.Exact
            : rival.Length != b.Length ? rival.Length < b.Length
            : string.CompareOrdinal(rival.Path, b.Path) < 0);
}
