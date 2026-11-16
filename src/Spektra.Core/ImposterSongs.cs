namespace Spektra.Core;

/// How a checked copy compares with the reference file of its song. Evidence, never a verdict on
/// the file: a copy that is not the reference recording can be another master, an edit or a real
/// alternate version. A copy with several reference files to choose from keeps its best grade, by
/// the preference ImposterSongs writes out for each grade.
public enum CopyGrade { Confirmed, OtherLength, PartlySame, DoesntLineUp, CantCompare }

public enum SongVerdict { AllConfirmed, SomeConfirmed, NoneConfirmed }

/// A checked file and the reference files that carry its name.
public sealed record CopyCandidates(ScannedFile Copy, IReadOnlyList<ScannedFile> References);

public sealed record ImposterCandidates(
    IReadOnlyList<CopyCandidates> Copies, IReadOnlyList<ScannedFile> NoReference, IReadOnlyList<ScannedFile> ReferenceFiles);

/// AlignConfidence: the alignment confidence against the chosen reference file, null when the
/// shared audio group confirmed the copy or nothing could be compared. SameAudioAs: another file
/// in an unconfirmed copy's audio group, null when there is none or the copy is confirmed.
public sealed record GradedCopy(ScannedFile File, CopyGrade Grade, double? AlignConfidence, string? SameAudioAs);

public sealed record ImposterSong(
    ScannedFile Reference, IReadOnlyList<GradedCopy> Copies, SongVerdict Verdict, QualityRanking? Best);

/// Roots: the folders to check, in the order given, the reference folder not among them.
public sealed record ImposterResult(
    string ReferenceRoot, IReadOnlyList<string> Roots, IReadOnlyList<ImposterSong> Songs,
    IReadOnlyList<ScannedFile> NoReference, IReadOnlyList<ScannedFile> ReferenceOnly, int FilesScanned);

/// Checks folders against a trusted reference folder, song by song: which checked copies are the
/// reference recording, how the others differ, and which confirmed copy has the best real quality.
/// It reads only what the duplicate scan produced, plus the alignments the caller runs for
/// PairsToAlign, so it decodes no audio and touches no file.
public static class ImposterSongs
{
    /// The reference files each checked file may be a copy of. A file is a reference file when its
    /// root is the reference root, ignoring case, and a checked file otherwise. A checked file's
    /// candidates are the reference files it twins with across roots at TitleTwin.NameRank 0 or 1:
    /// the same name, or the same title with one artist list inside the other.
    ///
    /// Both ranks count together, where SuspectPairs asks only the closest names: two different
    /// tracks can share a title and a first artist (a solo and a collaboration), so the copy's audio
    /// decides which of them it is, not the closer name. Rank 2 never counts, because a version word
    /// apart (a radio edit against an extended mix) names another recording.
    ///
    /// Copies lists the checked files that have a candidate, in the order of files, each with its
    /// references in ordinal path order. NoReference lists the checked files with none, and
    /// ReferenceFiles every reference file, both in the order of files.
    public static ImposterCandidates Candidates(IReadOnlyList<ScannedFile> files, string referenceRoot)
    {
        var references = files.Select(_ => new List<ScannedFile>()).ToArray();
        foreach (var (a, b) in TitleTwin.CrossRootTwins([.. files.Select(f => (f.Path, f.Root))]))
        {
            // Twins never share a root, so a pair holds at most one reference file. A pair of two
            // checked files is skipped: a copy is judged against the reference folder only.
            var (r, c) = IsReference(files[a], referenceRoot) ? (a, b) : (b, a);
            if (IsReference(files[r], referenceRoot) && TitleTwin.NameRank(files[r].Path, files[c].Path) <= 1)
                references[c].Add(files[r]);
        }

        var copies = new List<CopyCandidates>();
        var noReference = new List<ScannedFile>();
        for (var i = 0; i < files.Count; i++)
        {
            if (IsReference(files[i], referenceRoot)) continue;
            if (references[i].Count == 0) noReference.Add(files[i]);
            else copies.Add(new CopyCandidates(files[i], [.. references[i].OrderBy(f => f.Path, StringComparer.Ordinal)]));
        }
        return new ImposterCandidates(copies, noReference, [.. files.Where(f => IsReference(f, referenceRoot))]);
    }

    /// The alignments Build needs, as (reference, copy) pairs: every candidate of each copy that
    /// shares its audio group with none of them, in the order of Copies and References. A copy that
    /// shares its group with a candidate is that recording already and costs no audio work.
    public static IReadOnlyList<(ScannedFile Reference, ScannedFile Copy)> PairsToAlign(ImposterCandidates candidates) =>
        [.. candidates.Copies
            .Where(c => !c.References.Any(r => SameGroup(r, c.Copy)))
            .SelectMany(c => c.References.Select(r => (r, c.Copy)))];

    /// How a copy compares with one reference file. A shared audio group confirms the copy whatever
    /// else is passed, because the duplicate scan already grouped the two files by their audio.
    /// Otherwise the alignment label decides, and no alignment at all cannot compare. Alignment reads
    /// only the first seconds, so a copy that lines up as the same performance while its length
    /// differs by SuspectView.LengthGapSeconds or more is OtherLength (an edit, say), not confirmed.
    /// An unknown length is no gap.
    public static CopyGrade Grade(bool sameGroup, SuspectVerdict? alignment, double? copySeconds, double? referenceSeconds)
    {
        if (sameGroup) return CopyGrade.Confirmed;
        // Every label is named and any other value throws, so a label added later is graded on purpose.
        return alignment?.Label switch
        {
            null or SuspectLabel.CantCompare => CopyGrade.CantCompare,
            SuspectLabel.DoesntLineUp => CopyGrade.DoesntLineUp,
            SuspectLabel.PartlySame => CopyGrade.PartlySame,
            SuspectLabel.SamePerformance =>
                copySeconds is { } c && referenceSeconds is { } r && Math.Abs(c - r) >= SuspectView.LengthGapSeconds
                    ? CopyGrade.OtherLength
                    : CopyGrade.Confirmed,
            _ => throw new ArgumentOutOfRangeException(nameof(alignment), alignment, null),
        };
    }

    /// Each copy joins one reference file and is graded against it, and each reference file a copy
    /// joined is a song.
    ///
    /// A copy whose audio group holds candidates is Confirmed against the one the scan found most
    /// similar (then the lowest ordinal path), and alignmentOf is never called for it. Any other copy
    /// is graded against each candidate from alignmentOf(reference path, copy path) and keeps the
    /// best: the better grade, then the higher confidence, then the lowest ordinal path.
    ///
    /// Songs come in reference path order, and a song's copies in the order of their roots in roots
    /// (a root not listed goes last), then by path, both ignoring case. Best ranks the reference file
    /// and the Confirmed copies that have an audit row, and is null when none has: a copy that is not
    /// confirmed may sound better, but it is not a better copy of this recording. An unconfirmed copy
    /// in an audio group names another file of that group as SameAudioAs, so the reader sees where
    /// its audio is.
    public static ImposterResult Build(
        ImposterCandidates candidates, IReadOnlyList<ScannedFile> files, string referenceRoot, IReadOnlyList<string> roots,
        Func<string, string, SuspectVerdict?> alignmentOf, Func<string, string, double> similarity, int filesScanned)
    {
        var byGroup = files.Where(f => f.Group is not null).ToLookup(f => f.Group.GetValueOrDefault());
        var songs = candidates.Copies
            .Select(c =>
            {
                var (reference, grade, confidence) = Choose(c, alignmentOf, similarity);
                var sameAudioAs = grade == CopyGrade.Confirmed ? null : SameAudioAs(byGroup, c.Copy, referenceRoot);
                return (Reference: reference, Copy: new GradedCopy(c.Copy, grade, confidence, sameAudioAs));
            })
            .GroupBy(chosen => chosen.Reference, chosen => chosen.Copy)
            .OrderBy(song => song.Key.Path, StringComparer.OrdinalIgnoreCase)
            .Select(song => Song(song.Key, [.. song
                .OrderBy(c => RootOrder(roots, c.File.Root))
                .ThenBy(c => c.File.Path, StringComparer.OrdinalIgnoreCase)], similarity))
            .ToList();

        var joined = songs.Select(s => s.Reference).ToHashSet();
        return new ImposterResult(
            referenceRoot, roots, songs, candidates.NoReference,
            [.. candidates.ReferenceFiles.Where(f => !joined.Contains(f))], filesScanned);
    }

    /// The reference file a copy joins, how the copy compares with it, and the alignment confidence
    /// behind that.
    private static (ScannedFile Reference, CopyGrade Grade, double? Confidence) Choose(
        CopyCandidates candidates, Func<string, string, SuspectVerdict?> alignmentOf, Func<string, string, double> similarity)
    {
        var copy = candidates.Copy;
        var mates = candidates.References.Where(r => SameGroup(r, copy)).ToList();
        if (mates.Count > 0)
            return (mates
                .OrderByDescending(r => similarity(r.Path, copy.Path))
                .ThenBy(r => r.Path, StringComparer.Ordinal)
                .First(), CopyGrade.Confirmed, null);

        return candidates.References
            .Select(r =>
            {
                var verdict = alignmentOf(r.Path, copy.Path);
                return (Reference: r, Grade: Grade(false, verdict, copy.Seconds, r.Seconds), Confidence: verdict?.Confidence);
            })
            // Preference names each grade's place, and a null confidence sorts below every number.
            .OrderBy(x => Preference(x.Grade))
            .ThenByDescending(x => x.Confidence)
            .ThenBy(x => x.Reference.Path, StringComparer.Ordinal)
            .First();
    }

    /// A grade's place when a copy chooses among reference files, best first. Every grade is named
    /// here, not read off the order the enum declares them in, and any other value throws, so a
    /// grade added later is placed on purpose.
    private static int Preference(CopyGrade grade) => grade switch
    {
        CopyGrade.Confirmed => 0,
        CopyGrade.OtherLength => 1,
        CopyGrade.PartlySame => 2,
        CopyGrade.DoesntLineUp => 3,
        CopyGrade.CantCompare => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(grade), grade, null),
    };

    private static ImposterSong Song(
        ScannedFile reference, IReadOnlyList<GradedCopy> copies, Func<string, string, double> similarity)
    {
        var confirmed = copies.Where(c => c.Grade == CopyGrade.Confirmed).Select(c => c.File).ToList();
        var verdict = confirmed.Count == copies.Count ? SongVerdict.AllConfirmed
            : confirmed.Count > 0 ? SongVerdict.SomeConfirmed
            : SongVerdict.NoneConfirmed;
        List<(string Path, AuditRow Row)> ranked =
            [.. confirmed.Prepend(reference).Where(f => f.Row is not null).Select(f => (f.Path, f.Row!))];
        return new ImposterSong(reference, copies, verdict, ranked.Count == 0 ? null : QualityRanker.Rank(ranked, similarity));
    }

    /// Another file of the copy's audio group, null when the copy is in no group. A reference file
    /// comes first, because it tells which song of the reference folder the copy's audio is; then a
    /// file under a root other than the copy's, then the lowest ordinal path.
    private static string? SameAudioAs(ILookup<int, ScannedFile> byGroup, ScannedFile copy, string referenceRoot) =>
        copy.Group is not { } group ? null
        : byGroup[group]
            .Where(f => !string.Equals(f.Path, copy.Path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => IsReference(f, referenceRoot))
            .ThenBy(f => string.Equals(f.Root, copy.Root, StringComparison.OrdinalIgnoreCase))
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .Select(f => f.Path)
            .FirstOrDefault();

    private static bool IsReference(ScannedFile file, string referenceRoot) =>
        string.Equals(file.Root, referenceRoot, StringComparison.OrdinalIgnoreCase);

    /// Two files in no group never share one.
    private static bool SameGroup(ScannedFile a, ScannedFile b) => a.Group is { } group && b.Group == group;

    /// The place of a root in the list the caller gave, counted as the roots before it: all of them
    /// when it is not listed, so it goes last.
    private static int RootOrder(IReadOnlyList<string> roots, string root) =>
        roots.TakeWhile(r => !string.Equals(r, root, StringComparison.OrdinalIgnoreCase)).Count();
}
