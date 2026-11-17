using System.Globalization;
using System.Text;

namespace Spektra.Core;

/// What Imposter Buster says about each song, in what order, and the listening playlist it
/// exports, all in one place so the window and the CLI cannot drift apart. The wording describes
/// evidence and never calls a file fake: the listener's ear decides.
public static class ImposterView
{
    /// What a grade can and cannot tell.
    public const string GradeLimit =
        "A grade describes evidence, never a verdict on the file. A copy is confirmed when its fingerprint is all but identical to the reference file's, or when their first 10 s line up and their lengths agree. A fingerprint that only matches says the same song and arrangement, which another master and a careful re-recording both give. Another intro or a larger offset also reads as doesn't line up.";

    /// A grade as a row shows it: the evidence, never a verdict. Every grade is named here and any
    /// other value throws, so a grade added later is worded on purpose.
    public static string GradeText(CopyGrade grade) => grade switch
    {
        CopyGrade.Confirmed => "The reference recording",
        CopyGrade.SameByFingerprint => "Same by fingerprint, but the openings don't line up: another master, an edit, or a re-recording",
        CopyGrade.OtherLength => "Same opening, another length: an edit, another cut, or a cut-off file",
        CopyGrade.PartlySame => "Partly the same: another mix or edit",
        CopyGrade.DoesntLineUp => "Doesn't line up: a cover, another song, or another master",
        CopyGrade.CantCompare => "Can't compare: a copy is broken or too short",
        _ => throw new ArgumentOutOfRangeException(nameof(grade), grade, null),
    };

    /// A grade as the playlist titles abbreviate it.
    public static string ShortGrade(CopyGrade grade) => grade switch
    {
        CopyGrade.Confirmed => "reference recording",
        CopyGrade.SameByFingerprint => "same by fingerprint",
        CopyGrade.OtherLength => "other length",
        CopyGrade.PartlySame => "partly the same",
        CopyGrade.DoesntLineUp => "doesn't line up",
        CopyGrade.CantCompare => "can't compare",
        _ => throw new ArgumentOutOfRangeException(nameof(grade), grade, null),
    };

    /// What a song's copies add up to.
    public static string VerdictText(SongVerdict verdict) => verdict switch
    {
        SongVerdict.AllConfirmed => "Every copy is confirmed as the reference recording",
        SongVerdict.SomeConfirmed => "A copy is not confirmed as the reference recording",
        SongVerdict.NoneConfirmed => "No copy is confirmed as the reference recording",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, null),
    };

    /// What a song's copies add up to, short enough for a grid column; VerdictText is the sentence.
    public static string ShortVerdict(SongVerdict verdict) => verdict switch
    {
        SongVerdict.NoneConfirmed => "No copy confirmed",
        SongVerdict.SomeConfirmed => "A copy differs",
        SongVerdict.AllConfirmed => "All clear",
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, null),
    };

    /// The song as the reference folder names it: its reference file's name without the extension.
    public static string SongName(ImposterSong song) => Path.GetFileNameWithoutExtension(song.Reference.Path);

    /// A length such as "7:04", or "?" when it is not known.
    public static string Length(double? seconds) =>
        seconds is { } s ? AudioMetadata.FormatDuration(TimeSpan.FromSeconds(s)) : "?";

    /// True when the copy's and the reference file's lengths are both known and differ by
    /// SuspectView.LengthGapSeconds or more.
    public static bool HasLengthGap(GradedCopy copy, ImposterSong song) =>
        copy.File.Seconds is { } c && song.Reference.Seconds is { } r && Math.Abs(c - r) >= SuspectView.LengthGapSeconds;

    /// A file's quality as the duplicate groups word it; empty for a file the scan could not analyze.
    public static string Quality(AuditRow? row) => row is null ? "" : QualityRanker.Describe(row);

    /// A folder as Imposter Buster labels it: by what tells it apart from the reference folder and
    /// the other checked folders. A root that is none of them stands for itself.
    public static string FolderLabel(string root, string referenceRoot, IReadOnlyList<string> roots)
    {
        IReadOnlyList<string> all = [referenceRoot, .. roots];
        return SuspectView.LabelOf(root, all, ScopeLabel.Distinguish(all));
    }

    /// For a copy whose audio the scan found in another file too, a note such as "the same audio as
    /// x.flac in Rips"; null when there is none.
    public static string? SameAudioNote(GradedCopy copy, string referenceRoot, IReadOnlyList<string> roots) =>
        copy.SameAudioAs is { } same
            ? $"the same audio as {Path.GetFileName(same)} in {FolderOf(same, referenceRoot, roots)}"
            : null;

    /// The song's best copy: each winner as "the reference file" or "Old: x.flac", winners that tie
    /// joined by " = ", then the ranker's reason. With one file ranked the reason is left out,
    /// because the ranker then reads "identical copies, keep either", which would be wrong here.
    public static string BestText(ImposterSong song, string referenceRoot, IReadOnlyList<string> roots)
    {
        if (song.Best is not { } best) return "no copy could be ranked";

        var winners = string.Join(" = ", best.Winners.Select(path => path == song.Reference.Path
            ? "the reference file"
            : $"{FolderOf(path, referenceRoot, roots)}: {Path.GetFileName(path)}"));
        return best.OrderedPaths.Count > 1 ? $"{winners} · {best.Reason}" : winners;
    }

    /// The songs whose files hold every filter word (ignoring case; the words may land on different
    /// files, the reference file or any copy), in the order they are listed: no copy confirmed
    /// first, then a copy that differs, then all clear; inside each, by the song's most worrying
    /// copy, then by name ignoring case, then by the reference file's path.
    public static IReadOnlyList<ImposterSong> Ordered(IEnumerable<ImposterSong> songs, IReadOnlyCollection<string> tokens) =>
        [.. songs
            .Where(s => HoldsEveryWord(s, tokens))
            .OrderBy(s => Rank(s.Verdict))
            .ThenBy(s => s.Copies.Min(c => Rank(c.Grade)))
            .ThenBy(SongName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Reference.Path, StringComparer.Ordinal)];

    /// True when the path holds every filter word, ignoring case; a filter with no words holds them all.
    public static bool HoldsEveryWord(string path, IReadOnlyCollection<string> tokens) =>
        tokens.All(t => path.Contains(t, StringComparison.OrdinalIgnoreCase));

    /// What the check found, counted in one line.
    public static string Summary(ImposterResult result)
    {
        int Songs(SongVerdict verdict) => result.Songs.Count(s => s.Verdict == verdict);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{result.Songs.Count} song(s) · {Songs(SongVerdict.NoneConfirmed)} with no copy confirmed"
            + $" · {Songs(SongVerdict.SomeConfirmed)} with a copy that differs · {Songs(SongVerdict.AllConfirmed)} all clear"
            + $" · {result.NoReference.Count} file(s) with no reference"
            + $" · {result.ReferenceOnly.Count} reference file(s) not in the checked folders · {result.FilesScanned} scanned");
    }

    /// An extended M3U of the songs shown that have a copy which is not the reference recording, in
    /// the order given and numbered from 1. Each song is its unconfirmed copies in their order, then
    /// its reference file, so a player walks each copy and then the recording it should be. A copy
    /// in the audio group of one already listed for the song is left out: the same audio twice.
    /// Lines end in \n; the caller writes it as UTF-8 without a byte-order mark.
    public static string Playlist(IEnumerable<ImposterSong> shown, string referenceRoot, IReadOnlyList<string> roots)
    {
        var text = new StringBuilder("#EXTM3U\n");
        var number = 0;
        foreach (var song in shown)
        {
            var unconfirmed = song.Copies.Where(c => c.Grade != CopyGrade.Confirmed).ToList();
            if (unconfirmed.Count == 0) continue;

            number++;
            var name = SongName(song);
            // A copy in no group was matched with nothing, so only a shared group is the same audio.
            var groups = new HashSet<int>();
            foreach (var copy in unconfirmed)
            {
                if (copy.File.Group is { } group && !groups.Add(group)) continue;
                var how = $"{FolderLabel(copy.File.Root, referenceRoot, roots)} · {ShortGrade(copy.Grade)}";
                AppendEntry(text, number, how, name, copy.File);
            }
            AppendEntry(text, number, "reference", name, song.Reference);
        }
        return text.ToString();
    }

    /// The best copy of every song as a plain list of paths, to build a library from: one line per
    /// song with a best copy, in the order of result.Songs, each the first of the song's winners
    /// (the ranker's own order settles a tie, so each song gives exactly one file). The reference
    /// file is listed where it is the best copy; a song with nothing ranked has no line. Paths are
    /// written as the result holds them and lines end in \n; the caller writes it as UTF-8 without
    /// a byte-order mark.
    public static string BestCopiesList(ImposterResult result)
    {
        var text = new StringBuilder();
        foreach (var song in result.Songs)
            if (song.Best?.Winners.FirstOrDefault() is { } best) text.Append(best).Append('\n');
        return text.ToString();
    }

    // Each verdict's and grade's place is written out, never read off the enum's order, and any
    // other value throws, so one added later takes the place it should. What most needs a listen
    // comes first.
    private static int Rank(SongVerdict verdict) => verdict switch
    {
        SongVerdict.NoneConfirmed => 0,
        SongVerdict.SomeConfirmed => 1,
        SongVerdict.AllConfirmed => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, null),
    };

    private static int Rank(CopyGrade grade) => grade switch
    {
        CopyGrade.DoesntLineUp => 0,
        CopyGrade.CantCompare => 1,
        CopyGrade.PartlySame => 2,
        CopyGrade.OtherLength => 3,
        CopyGrade.SameByFingerprint => 4,
        CopyGrade.Confirmed => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(grade), grade, null),
    };

    // The song's name is part of its reference file's path, so the paths alone decide.
    private static bool HoldsEveryWord(ImposterSong song, IReadOnlyCollection<string> tokens) =>
        tokens.All(t => song.Reference.Path.Contains(t, StringComparison.OrdinalIgnoreCase)
                        || song.Copies.Any(c => c.File.Path.Contains(t, StringComparison.OrdinalIgnoreCase)));

    /// The label of the folder a file sits in, among the reference folder and the checked folders.
    private static string FolderOf(string path, string referenceRoot, IReadOnlyList<string> roots) =>
        FolderLabel(DuplicateScan.RootOf(path, [referenceRoot, .. roots]), referenceRoot, roots);

    private static void AppendEntry(StringBuilder text, int number, string how, string name, ScannedFile file)
    {
        // Whole seconds (a half rounds up), -1 where the length is not known.
        var length = file.Seconds is { } s ? (long)Math.Round(s, MidpointRounding.AwayFromZero) : -1;
        text.Append(string.Create(CultureInfo.InvariantCulture, $"#EXTINF:{length},{number} {how} · {name}\n"))
            .Append(Path.GetFullPath(file.Path)).Append('\n');
    }
}
