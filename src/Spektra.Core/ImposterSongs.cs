namespace Spektra.Core;

/// How a checked copy compares with the reference file of its song. Evidence, never a verdict on
/// the file: a copy that is not the reference recording can be another master, an edit or a real
/// alternate version. A copy with several reference files to choose from keeps its best grade, by
/// the preference ImposterSongs writes out for each grade.
public enum CopyGrade { Confirmed, SameByFingerprint, OtherLength, PartlySame, DoesntLineUp, CantCompare }

public enum SongVerdict { AllConfirmed, SomeConfirmed, NoneConfirmed }

/// A checked file and the reference files that carry its name.
public sealed record CopyCandidates(ScannedFile Copy, IReadOnlyList<ScannedFile> References);

public sealed record ImposterCandidates(
    IReadOnlyList<CopyCandidates> Copies, IReadOnlyList<ScannedFile> NoReference, IReadOnlyList<ScannedFile> ReferenceFiles);

/// AlignConfidence: the alignment confidence against the chosen reference file, null when an
/// identical fingerprint confirmed the copy or nothing could be compared. SameAudioAs: another file
/// in an unconfirmed copy's audio group than the reference file it joined, null when there is none
/// or the copy is confirmed.
public sealed record GradedCopy(ScannedFile File, CopyGrade Grade, double? AlignConfidence, string? SameAudioAs)
{
    /// The scan's fingerprint similarity between the copy and the reference file it joined, 0 when
    /// the scan did not match the two.
    public double Similarity { get; init; }
}

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
    /// From this fingerprint similarity a copy is the reference file's audio across its whole
    /// length, and is confirmed without an alignment.
    public const double IdenticalFrom = QualityRanker.IdenticalSimilarity;

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
    /// A twin with one name inside the other must share three words, which shuts out a short name
    /// such as Artist - Title against the reference file that credits every artist. So a checked
    /// file and a reference file are candidates too, however few words they share, when both names
    /// have artists and a title, the titles hold the same words and one artist list is inside the
    /// other. A name with no artists never pairs this way: a file called only Intro is not a copy of
    /// every Artist - Intro.
    ///
    /// Copies lists the checked files that have a candidate, in the order of files, each with its
    /// references in ordinal path order, each once whichever rule found it. NoReference lists the
    /// checked files with none, and ReferenceFiles every reference file, both in the order of files.
    public static ImposterCandidates Candidates(IReadOnlyList<ScannedFile> files, string referenceRoot)
    {
        // Indexes into files, as (copy, reference).
        var pairs = new HashSet<(int Copy, int Reference)>();
        foreach (var (a, b) in TitleTwin.CrossRootTwins([.. files.Select(f => (f.Path, f.Root))]))
        {
            // Twins never share a root, so a pair holds at most one reference file. A pair of two
            // checked files is skipped: a copy is judged against the reference folder only.
            var (r, c) = IsReference(files[a], referenceRoot) ? (a, b) : (b, a);
            if (IsReference(files[r], referenceRoot) && TitleTwin.NameRank(files[r].Path, files[c].Path) <= 1)
                pairs.Add((c, r));
        }

        // The reference files by their title, so a checked file meets only those with its own.
        var names = files.Select(f => TitleTwin.ArtistsAndTitle(f.Path)).ToArray();
        var byTitle = Enumerable.Range(0, files.Count)
            .Where(i => IsReference(files[i], referenceRoot) && names[i].Artists.Count > 0 && names[i].Title.Count > 0)
            .ToLookup(i => TitleKey(names[i].Title));
        for (var c = 0; c < files.Count; c++)
        {
            var (artists, title) = names[c];
            if (IsReference(files[c], referenceRoot) || artists.Count == 0 || title.Count == 0) continue;
            foreach (var r in byTitle[TitleKey(title)])
                if (artists.IsSubsetOf(names[r].Artists) || names[r].Artists.IsSubsetOf(artists))
                    pairs.Add((c, r));
        }

        var references = pairs.ToLookup(p => p.Copy, p => files[p.Reference]);
        var copies = new List<CopyCandidates>();
        var noReference = new List<ScannedFile>();
        for (var i = 0; i < files.Count; i++)
        {
            if (IsReference(files[i], referenceRoot)) continue;
            if (!references[i].Any()) noReference.Add(files[i]);
            else copies.Add(new CopyCandidates(files[i], [.. references[i].OrderBy(f => f.Path, StringComparer.Ordinal)]));
        }
        return new ImposterCandidates(copies, noReference, [.. files.Where(f => IsReference(f, referenceRoot))]);
    }

    /// The alignments Build needs, as (reference, copy) pairs: every candidate of each copy none of
    /// whose candidates reaches IdenticalFrom in similarity(reference path, copy path), in the order
    /// of Copies and References. A copy with an identical candidate is that recording already and
    /// costs no audio work.
    public static IReadOnlyList<(ScannedFile Reference, ScannedFile Copy)> PairsToAlign(
        ImposterCandidates candidates, Func<string, string, double> similarity) =>
        [.. candidates.Copies
            .Where(c => IdenticalReference(c, similarity) is null)
            .SelectMany(c => c.References.Select(r => (r, c.Copy)))];

    /// How a copy compares with one reference file. A fingerprint similarity of IdenticalFrom or more
    /// confirms the copy whatever else is passed: the two hold the same audio across their whole
    /// length. Otherwise the alignment label decides, and no alignment at all cannot compare.
    /// Alignment reads only the first seconds, so a copy that lines up as the same performance while
    /// its length differs by SuspectView.LengthGapSeconds or more is OtherLength (an edit, say), not
    /// confirmed. An unknown length is no gap.
    ///
    /// A shared audio group says only that the fingerprints match: the same song and arrangement,
    /// which another master and a careful re-recording both give. So it never confirms a copy and
    /// never hides one that cannot be compared: it only turns an opening that lines up in part or
    /// not at all into SameByFingerprint.
    public static CopyGrade Grade(
        double similarity, bool sameGroup, SuspectVerdict? alignment, double? copySeconds, double? referenceSeconds)
    {
        if (similarity >= IdenticalFrom) return CopyGrade.Confirmed;
        // Every label is named and any other value throws, so a label added later is graded on purpose.
        return alignment?.Label switch
        {
            null or SuspectLabel.CantCompare => CopyGrade.CantCompare,
            SuspectLabel.DoesntLineUp => sameGroup ? CopyGrade.SameByFingerprint : CopyGrade.DoesntLineUp,
            SuspectLabel.PartlySame => sameGroup ? CopyGrade.SameByFingerprint : CopyGrade.PartlySame,
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
    /// A copy with a candidate that reaches IdenticalFrom in similarity is Confirmed against the most
    /// similar such candidate (then the lowest ordinal path), and alignmentOf is never called for it.
    /// Any other copy is graded against each candidate from alignmentOf(reference path, copy path),
    /// their similarity and whether they share an audio group, and keeps the best: the better grade,
    /// then the higher confidence, then the lowest ordinal path. A copy's Similarity is the one with
    /// the reference file it joined.
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
                var sameAudioAs = grade == CopyGrade.Confirmed ? null : SameAudioAs(byGroup, c.Copy, reference, referenceRoot);
                return (Reference: reference, Copy: new GradedCopy(c.Copy, grade, confidence, sameAudioAs)
                {
                    Similarity = similarity(reference.Path, c.Copy.Path),
                });
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
        if (IdenticalReference(candidates, similarity) is { } identical) return (identical, CopyGrade.Confirmed, null);

        return candidates.References
            .Select(r =>
            {
                var verdict = alignmentOf(r.Path, copy.Path);
                var grade = Grade(similarity(r.Path, copy.Path), SameGroup(r, copy), verdict, copy.Seconds, r.Seconds);
                return (Reference: r, Grade: grade, Confidence: verdict?.Confidence);
            })
            // Preference names each grade's place, and a null confidence sorts below every number.
            .OrderBy(x => Preference(x.Grade))
            .ThenByDescending(x => x.Confidence)
            .ThenBy(x => x.Reference.Path, StringComparer.Ordinal)
            .First();
    }

    /// The candidate a copy is all but identical to: the most similar of those that reach
    /// IdenticalFrom, then the lowest ordinal path; null when none does. PairsToAlign and Build both
    /// ask here, so the copies one leaves out are exactly the ones the other confirms unaligned.
    private static ScannedFile? IdenticalReference(CopyCandidates candidates, Func<string, string, double> similarity) =>
        candidates.References
            .Select(r => (Reference: r, Similarity: similarity(r.Path, candidates.Copy.Path)))
            .Where(x => x.Similarity >= IdenticalFrom)
            .OrderByDescending(x => x.Similarity)
            .ThenBy(x => x.Reference.Path, StringComparer.Ordinal)
            .Select(x => x.Reference)
            .FirstOrDefault();

    /// A grade's place when a copy chooses among reference files, best first. Every grade is named
    /// here, not read off the order the enum declares them in, and any other value throws, so a
    /// grade added later is placed on purpose.
    private static int Preference(CopyGrade grade) => grade switch
    {
        CopyGrade.Confirmed => 0,
        CopyGrade.SameByFingerprint => 1,
        CopyGrade.OtherLength => 2,
        CopyGrade.PartlySame => 3,
        CopyGrade.DoesntLineUp => 4,
        CopyGrade.CantCompare => 5,
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

    /// Another file of the copy's audio group than the copy and the reference file it joined, whose
    /// grade already says how the two compare; null when there is none. A reference file comes
    /// first, because it tells which song of the reference folder the copy's audio is; then a file
    /// under a root other than the copy's, then the lowest ordinal path.
    private static string? SameAudioAs(
        ILookup<int, ScannedFile> byGroup, ScannedFile copy, ScannedFile reference, string referenceRoot) =>
        copy.Group is not { } group ? null
        : byGroup[group]
            .Where(f => !string.Equals(f.Path, copy.Path, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(f.Path, reference.Path, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => IsReference(f, referenceRoot))
            .ThenBy(f => string.Equals(f.Root, copy.Root, StringComparison.OrdinalIgnoreCase))
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .Select(f => f.Path)
            .FirstOrDefault();

    private static bool IsReference(ScannedFile file, string referenceRoot) =>
        string.Equals(file.Root, referenceRoot, StringComparison.OrdinalIgnoreCase);

    /// Two files in no group never share one.
    private static bool SameGroup(ScannedFile a, ScannedFile b) => a.Group is { } group && b.Group == group;

    /// A title as one string, its words in ordinal order. Two titles hold the same words exactly when
    /// their keys are equal, because a word is letters and digits only.
    private static string TitleKey(IReadOnlySet<string> title) => string.Join(' ', title.Order(StringComparer.Ordinal));

    /// The place of a root in the list the caller gave, counted as the roots before it: all of them
    /// when it is not listed, so it goes last.
    private static int RootOrder(IReadOnlyList<string> roots, string root) =>
        roots.TakeWhile(r => !string.Equals(r, root, StringComparison.OrdinalIgnoreCase)).Count();
}
