namespace Spektra.Core;

/// How a checked copy compares with the reference file of its song. Evidence, never a verdict on
/// the file: a copy that is not the reference recording can be another master, an edit or a real
/// alternate version. A copy with several reference files to choose from keeps its best grade, by
/// the preference ImposterSongs writes out for each grade.
public enum CopyGrade { Confirmed, SameByFingerprint, OtherLength, PartlySame, DoesntLineUp, CantCompare }

public enum SongVerdict { AllConfirmed, SomeConfirmed, NoneConfirmed }

/// How a checked file was found to be a candidate copy of a reference file, strongest first: the
/// two carry the same Spotify track ID, the same ISRC, or neither and their names pair them. It says
/// which track the files claim to be, never what audio they hold.
public enum MatchKind { SpotifyId, Isrc, Name }

/// A reference file a checked file may be a copy of, and the strongest way the two were paired.
public sealed record ReferenceCandidate(ScannedFile Reference, MatchKind MatchedBy);

/// A checked file and the reference files it may be a copy of, by ID or by name.
public sealed record CopyCandidates(ScannedFile Copy, IReadOnlyList<ReferenceCandidate> References);

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

    /// How the copy was paired with the reference file it joined.
    public MatchKind MatchedBy { get; init; } = MatchKind.Name;
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
    /// From this fingerprint similarity, between two files of one audio group, a copy is the
    /// reference file's audio across its whole length, and is confirmed without an alignment. In the
    /// 2026-11-17 calibration (6,052 real copies with both a similarity and an alignment) every one
    /// of the 4,682 at 0.93 or more also lined up (the lowest alignment read 0.90), and the most
    /// similar copy that matched by fingerprint without lining up read 0.917. A lossless file
    /// against a lossy encode of itself typically reads 0.94, so the duplicate ranker's 0.98 left
    /// nearly every such pair to the alignment.
    public const double IdenticalFrom = 0.93;

    /// The reference files each checked file may be a copy of, by the track IDs their tags carry and
    /// by their names. A file is a reference file when its root is the reference root, ignoring
    /// case, and a checked file otherwise.
    ///
    /// By ID: ids maps a file's path to the IDs its tags carry, and a file missing from it, or a null
    /// map, carries none. A checked file's candidates are the reference files that carry its Spotify
    /// track ID or its ISRC, each compared by ordinal: a Spotify ID is base62, so two that differ only
    /// in case are two tracks, and TrackIds keeps an ISRC normalized. Two checked files never pair
    /// this way, nor two reference files. An ID only adds candidates and never removes one: two files
    /// can carry different IDs and hold the same recording (a single and its album track share the
    /// recording, not the Spotify ID), and a name pair the IDs disagree with is still graded by its
    /// audio.
    ///
    /// By name: a checked file's candidates are also the reference files it twins with across roots
    /// at TitleTwin.NameRank 0 or 1: the same name, or the same title with one artist list inside
    /// the other.
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
    /// Each candidate says how it was paired, whichever rule found it: by the Spotify ID when the two
    /// share one, else by the ISRC when they share one, else by name. Copies lists the checked files
    /// that have a candidate, in the order of files, each with its references in ordinal path order,
    /// each once whichever rule found it. NoReference lists the checked files with no candidate by
    /// either, and ReferenceFiles every reference file, both in the order of files.
    public static ImposterCandidates Candidates(
        IReadOnlyList<ScannedFile> files, string referenceRoot, IReadOnlyDictionary<string, TrackIds>? ids = null)
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

        // The reference files by each ID they carry, so a checked file meets only those that share one.
        var idsOf = files.Select(f => ids?.GetValueOrDefault(f.Path) ?? TrackIds.None).ToArray();
        var referenceIndexes = Enumerable.Range(0, files.Count).Where(i => IsReference(files[i], referenceRoot)).ToArray();
        var bySpotifyId = referenceIndexes.Where(i => idsOf[i].SpotifyId is not null)
            .ToLookup(i => idsOf[i].SpotifyId!, StringComparer.Ordinal);
        var byIsrc = referenceIndexes.Where(i => idsOf[i].Isrc is not null)
            .ToLookup(i => idsOf[i].Isrc!, StringComparer.Ordinal);
        for (var c = 0; c < files.Count; c++)
        {
            if (IsReference(files[c], referenceRoot)) continue;
            if (idsOf[c].SpotifyId is { } spotifyId)
                foreach (var r in bySpotifyId[spotifyId]) pairs.Add((c, r));
            if (idsOf[c].Isrc is { } isrc)
                foreach (var r in byIsrc[isrc]) pairs.Add((c, r));
        }

        // The strongest ID the two share, whichever rule found the pair.
        MatchKind MatchedBy(int c, int r) =>
            SameId(idsOf[c].SpotifyId, idsOf[r].SpotifyId) ? MatchKind.SpotifyId
            : SameId(idsOf[c].Isrc, idsOf[r].Isrc) ? MatchKind.Isrc
            : MatchKind.Name;

        var references = pairs.ToLookup(p => p.Copy, p => new ReferenceCandidate(files[p.Reference], MatchedBy(p.Copy, p.Reference)));
        var copies = new List<CopyCandidates>();
        var noReference = new List<ScannedFile>();
        for (var i = 0; i < files.Count; i++)
        {
            if (IsReference(files[i], referenceRoot)) continue;
            if (!references[i].Any()) noReference.Add(files[i]);
            else copies.Add(new CopyCandidates(files[i], [.. references[i].OrderBy(r => r.Reference.Path, StringComparer.Ordinal)]));
        }
        return new ImposterCandidates(copies, noReference, [.. files.Where(f => IsReference(f, referenceRoot))]);
    }

    /// The alignments Build needs, as (reference, copy) pairs: every candidate of each copy that has
    /// no identical candidate, in the order of Copies and References. A copy with an identical
    /// candidate is that recording already and costs no audio work. A candidate is identical only
    /// when it shares the copy's audio group and reaches IdenticalFrom in similarity(reference path,
    /// copy path): the similarity can be high over a stretch two different files share, and only a
    /// pair the grouper put in one group matched across the whole length, so a high similarity
    /// outside the group is aligned like any other pair.
    public static IReadOnlyList<(ScannedFile Reference, ScannedFile Copy)> PairsToAlign(
        ImposterCandidates candidates, Func<string, string, double> similarity) =>
        [.. candidates.Copies
            .Where(c => IdenticalReference(c, similarity) is null)
            .SelectMany(c => c.References.Select(r => (r.Reference, c.Copy)))];

    /// How a copy compares with one reference file. A fingerprint similarity of IdenticalFrom or more
    /// in a shared audio group confirms the copy whatever else is passed: the two hold the same audio
    /// across their whole length. The group is part of the rule because the scan's similarity can be
    /// high over a stretch two different files share, and only a pair the grouper put in one group
    /// matched across the whole length, so a high similarity outside the group proves nothing and the
    /// alignment decides, as for any other pair. Otherwise the alignment label decides, and no
    /// alignment at all cannot compare. Alignment reads only the first seconds, so a copy that lines
    /// up as the same performance while its length differs by SuspectView.LengthGapSeconds or more is
    /// OtherLength (an edit, say), not confirmed. An unknown length is no gap.
    ///
    /// A shared audio group alone says only that the fingerprints match: the same song and
    /// arrangement, which another master and a careful re-recording both give. So below IdenticalFrom
    /// it never confirms a copy and never hides one that cannot be compared: it only turns an opening
    /// that lines up in part or not at all into SameByFingerprint.
    public static CopyGrade Grade(
        double similarity, bool sameGroup, SuspectVerdict? alignment, double? copySeconds, double? referenceSeconds)
    {
        if (sameGroup && similarity >= IdenticalFrom) return CopyGrade.Confirmed;
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
    /// joined is a song. The audio decides first and the ID second: how a candidate was matched (a
    /// shared Spotify ID, then a shared ISRC, then the name) breaks a tie between equal grades and
    /// never beats a better grade.
    ///
    /// A copy with a candidate that shares its audio group and reaches IdenticalFrom in similarity is
    /// Confirmed against the strongest matched such candidate, then the most similar (then the lowest
    /// ordinal path), and alignmentOf is never called for it. A candidate outside the group never
    /// counts, however similar: the scan's similarity can be high over a stretch two different files
    /// share, and only a pair the grouper put in one group matched across the whole length. Any other
    /// copy is graded against each candidate from alignmentOf(reference path, copy path), their
    /// similarity and whether they share an audio group, and keeps the best: the better grade, then
    /// the stronger match, then the higher confidence, then the lowest ordinal path. A copy's
    /// Similarity is the one with the reference file it joined, and its MatchedBy how the two were
    /// paired.
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
                var (chosen, grade, confidence) = Choose(c, alignmentOf, similarity);
                var reference = chosen.Reference;
                var sameAudioAs = grade == CopyGrade.Confirmed ? null : SameAudioAs(byGroup, c.Copy, reference, referenceRoot);
                return (Reference: reference, Copy: new GradedCopy(c.Copy, grade, confidence, sameAudioAs)
                {
                    Similarity = similarity(reference.Path, c.Copy.Path),
                    MatchedBy = chosen.MatchedBy,
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
    private static (ReferenceCandidate Candidate, CopyGrade Grade, double? Confidence) Choose(
        CopyCandidates candidates, Func<string, string, SuspectVerdict?> alignmentOf, Func<string, string, double> similarity)
    {
        var copy = candidates.Copy;
        if (IdenticalReference(candidates, similarity) is { } identical) return (identical, CopyGrade.Confirmed, null);

        return candidates.References
            .Select(candidate =>
            {
                var r = candidate.Reference;
                var verdict = alignmentOf(r.Path, copy.Path);
                var grade = Grade(similarity(r.Path, copy.Path), SameGroup(r, copy), verdict, copy.Seconds, r.Seconds);
                return (Candidate: candidate, Grade: grade, Confidence: verdict?.Confidence);
            })
            // Preference names each grade's place and Strength each match's, and a null confidence
            // sorts below every number.
            .OrderBy(x => Preference(x.Grade))
            .ThenBy(x => Strength(x.Candidate.MatchedBy))
            .ThenByDescending(x => x.Confidence)
            .ThenBy(x => x.Candidate.Reference.Path, StringComparer.Ordinal)
            .First();
    }

    /// The candidate a copy is all but identical to: of those in the copy's audio group that reach
    /// IdenticalFrom, the strongest match, then the most similar, then the lowest ordinal path; null
    /// when none does. The group is required because the scan's similarity can be high over a
    /// stretch two different files share, and only a pair the grouper put in one group matched
    /// across the whole length. PairsToAlign and Build both ask here, so the copies one leaves out
    /// are exactly the ones the other confirms unaligned.
    private static ReferenceCandidate? IdenticalReference(CopyCandidates candidates, Func<string, string, double> similarity) =>
        candidates.References
            .Select(c => (Candidate: c, Similarity: similarity(c.Reference.Path, candidates.Copy.Path)))
            .Where(x => SameGroup(x.Candidate.Reference, candidates.Copy) && x.Similarity >= IdenticalFrom)
            .OrderBy(x => Strength(x.Candidate.MatchedBy))
            .ThenByDescending(x => x.Similarity)
            .ThenBy(x => x.Candidate.Reference.Path, StringComparer.Ordinal)
            .Select(x => x.Candidate)
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

    /// A match's place when a copy chooses between reference files, strongest first, which only
    /// breaks a tie between equal grades. Every kind is named here, not read off the order the enum
    /// declares them in, and any other value throws, so a kind added later is placed on purpose.
    private static int Strength(MatchKind kind) => kind switch
    {
        MatchKind.SpotifyId => 0,
        MatchKind.Isrc => 1,
        MatchKind.Name => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
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

    /// Two IDs name one track when both are there and equal by ordinal: a Spotify ID is base62, so
    /// case tells two apart, and TrackIds keeps an ISRC normalized.
    private static bool SameId(string? a, string? b) => a is not null && string.Equals(a, b, StringComparison.Ordinal);

    /// A title as one string, its words in ordinal order. Two titles hold the same words exactly when
    /// their keys are equal, because a word is letters and digits only.
    private static string TitleKey(IReadOnlySet<string> title) => string.Join(' ', title.Order(StringComparer.Ordinal));

    /// The place of a root in the list the caller gave, counted as the roots before it: all of them
    /// when it is not listed, so it goes last.
    private static int RootOrder(IReadOnlyList<string> roots, string root) =>
        roots.TakeWhile(r => !string.Equals(r, root, StringComparison.OrdinalIgnoreCase)).Count();
}
