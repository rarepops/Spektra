using System.Globalization;
using System.Text;

namespace Spektra.Core;

/// What the "Same name, different audio" section says, in what order, and the
/// listening playlist it exports, all in one place so the window, the CLI and the
/// playlist cannot drift apart. The wording describes evidence and never calls a
/// copy fake: the listener's ear decides.
public static class SuspectView
{
    /// What the alignment check can and cannot tell, shown as a tooltip on a label.
    public const string AlignmentLimit =
        "Alignment can clear a pair, never convict one: only the first 10 s of each copy are compared, within ±2 s, so a different intro or a larger offset also reads as doesn't line up.";

    /// Two copies whose lengths differ by this many seconds or more are worth a look.
    public const double LengthGapSeconds = 10;

    /// The labels listed first. Every label is named here, not read off the enum's
    /// numbers, and any other value throws, so a label added later is placed on purpose.
    public static bool IsTopTier(SuspectLabel label) => label switch
    {
        SuspectLabel.DoesntLineUp or SuspectLabel.CantCompare => true,
        SuspectLabel.PartlySame or SuspectLabel.SamePerformance => false,
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, null),
    };

    /// A label as a row shows it: the evidence, never a verdict.
    public static string LabelText(SuspectLabel label) => label switch
    {
        SuspectLabel.DoesntLineUp => "Doesn't line up: a cover, another song, or a heavy remaster",
        SuspectLabel.CantCompare => "Can't compare: one copy is broken or too short",
        SuspectLabel.PartlySame => "Partly the same: another mix or edit",
        SuspectLabel.SamePerformance => "Same performance (another master, or identical)",
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, null),
    };

    /// A label as the playlist titles abbreviate it.
    public static string ShortLabel(SuspectLabel label) => label switch
    {
        SuspectLabel.DoesntLineUp => "doesn't line up",
        SuspectLabel.CantCompare => "can't compare",
        SuspectLabel.PartlySame => "partly the same",
        SuspectLabel.SamePerformance => "same performance",
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, null),
    };

    /// How closely the two names agree.
    public static string StrengthText(NameStrength strength) => strength switch
    {
        NameStrength.SameName => "same name",
        NameStrength.NearName => "near name",
        _ => throw new ArgumentOutOfRangeException(nameof(strength), strength, null),
    };

    /// The rows whose paths hold every filter word (ignoring case; the words may land
    /// on different sides, as a group's filter reads its members), in the order the
    /// section lists them: by label rank (a row with no label yet first), then same
    /// name before near name, then the file name of A ignoring case, then A's path,
    /// then B's path, which settles one A file paired with two B files.
    public static IReadOnlyList<T> Visible<T>(
        IEnumerable<T> rows, Func<T, SuspectPair> pairOf, Func<T, SuspectVerdict?> verdictOf,
        IReadOnlyCollection<string> tokens) =>
        [.. rows
            .Where(r => HoldsEveryWord(pairOf(r), tokens))
            .OrderBy(r => Rank(verdictOf(r)))
            .ThenBy(r => pairOf(r).Strength == NameStrength.NearName)
            .ThenBy(r => Path.GetFileName(pairOf(r).PathA), StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => pairOf(r).PathA, StringComparer.Ordinal)
            .ThenBy(r => pairOf(r).PathB, StringComparer.Ordinal)];

    /// The section's heading: how many pairs to listen to first, how many are similar,
    /// and how many have no label yet (still being checked, or left unchecked after a
    /// cancel). "none" when there is nothing at all.
    public static string Heading(int top, int bottom, int unlabelled, bool checking)
    {
        const string title = "Same name, different audio";
        if (top == 0 && bottom == 0 && unlabelled == 0) return $"{title} · none";

        var heading = $"{title} · {top} to listen to first · {bottom} similar";
        if (unlabelled == 0) return heading;
        return checking ? $"{heading} · {unlabelled} checking…" : $"{heading} · {unlabelled} not checked";
    }

    /// Both copies' lengths, such as "7:04 vs 2:54", with a ? where one is unknown.
    public static string Lengths(SuspectPair pair) =>
        $"{Length(pair.SecondsA)} vs {Length(pair.SecondsB)}";

    /// True when both lengths are known and differ by LengthGapSeconds or more.
    public static bool HasLengthGap(SuspectPair pair) =>
        pair is { SecondsA: { } a, SecondsB: { } b } && Math.Abs(a - b) >= LengthGapSeconds;

    /// For each side whose copy shares its audio with another file, a note such as "the
    /// copy in B is the same audio as x.flac in A"; A's note first, the two joined by a
    /// middle dot. Null when neither side has one. Folders are labelled as the columns
    /// label them.
    public static string? CrossMatch(SuspectPair pair, IReadOnlyList<string> roots)
    {
        var labels = ScopeLabel.Distinguish(roots);
        var notes = new List<string>(2);
        if (pair.SameAudioAsA is { } a) notes.Add(CrossNote(pair.RootA, a, roots, labels));
        if (pair.SameAudioAsB is { } b) notes.Add(CrossNote(pair.RootB, b, roots, labels));
        return notes.Count == 0 ? null : string.Join(" · ", notes);
    }

    /// An extended M3U of the first-tier pairs among those shown, in the order given and
    /// numbered from 1. Each pair is two entries, A then B, so a player walks the pair
    /// and the ear compares them. A pair with no verdict yet, or in the bottom tier, is
    /// left out. Lines end in \n; the caller writes it as UTF-8 without a byte-order mark.
    public static string Playlist(
        IEnumerable<(SuspectPair Pair, SuspectVerdict? Verdict)> shown, IReadOnlyList<string> roots)
    {
        var labels = ScopeLabel.Distinguish(roots);
        var text = new StringBuilder("#EXTM3U\n");
        var number = 0;
        foreach (var (pair, verdict) in shown)
        {
            if (verdict is not { } v || !IsTopTier(v.Label)) continue;

            number++;
            var how = ShortLabel(v.Label);
            AppendEntry(text, number, LabelOf(pair.RootA, roots, labels), how, pair.PathA, pair.SecondsA);
            AppendEntry(text, number, LabelOf(pair.RootB, roots, labels), how, pair.PathB, pair.SecondsB);
        }
        return text.ToString();
    }

    // Each label's place is written out, never read off the enum's numbers, so a label
    // added later takes the rank it should. A row with no label yet comes first.
    private static int Rank(SuspectVerdict? verdict) => verdict?.Label switch
    {
        null => 0,
        SuspectLabel.DoesntLineUp => 1,
        SuspectLabel.CantCompare => 2,
        SuspectLabel.PartlySame => 3,
        SuspectLabel.SamePerformance => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, null),
    };

    private static bool HoldsEveryWord(SuspectPair pair, IReadOnlyCollection<string> tokens) =>
        tokens.All(t => pair.PathA.Contains(t, StringComparison.OrdinalIgnoreCase)
                        || pair.PathB.Contains(t, StringComparison.OrdinalIgnoreCase));

    private static string Length(double? seconds) =>
        seconds is { } s ? AudioMetadata.FormatDuration(TimeSpan.FromSeconds(s)) : "?";

    private static string CrossNote(
        string root, string sameAudioAs, IReadOnlyList<string> roots, IReadOnlyList<string> labels) =>
        $"the copy in {LabelOf(root, roots, labels)} is the same audio as {Path.GetFileName(sameAudioAs)} "
        + $"in {LabelOf(DuplicateScan.RootOf(sameAudioAs, roots), roots, labels)}";

    /// The label ScopeLabel.Distinguish gave a root (labels[i] for roots[i], matched ignoring case);
    /// a root that is not in the list stands for itself.
    public static string LabelOf(string root, IReadOnlyList<string> roots, IReadOnlyList<string> labels)
    {
        for (var i = 0; i < roots.Count; i++)
            if (string.Equals(roots[i], root, StringComparison.OrdinalIgnoreCase)) return labels[i];
        return root;
    }

    private static void AppendEntry(
        StringBuilder text, int number, string folder, string how, string path, double? seconds)
    {
        // Whole seconds (a half rounds up), -1 where the length is not known.
        var length = seconds is { } s ? (long)Math.Round(s, MidpointRounding.AwayFromZero) : -1;
        text.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"#EXTINF:{length},{number} {folder} · {how} · {Path.GetFileNameWithoutExtension(path)}\n"))
            .Append(Path.GetFullPath(path)).Append('\n');
    }
}
