using Spektra.Core;

namespace Spektra.Tests;

public sealed class ImposterSongsTests
{
    private const string Rips = "/music/Rips", Old = "/music/Old", New = "/music/New";
    private static readonly string[] Checked = [Old, New];
    private static readonly Func<string, string, double> NoSim = (_, _) => 0;

    // For copies their audio group confirms, which must never be aligned.
    private static readonly Func<string, string, SuspectVerdict?> NoAlignment =
        (_, _) => throw new InvalidOperationException("alignmentOf was called");

    private static ScannedFile F(string root, string name, int? group = null, double? seconds = 200, AuditRow? row = null) =>
        new($"{root}/{name}", root, group, seconds) { Row = row, SizeBytes = 1000 };

    private static AuditRow Row(string codec, string bandwidth, double? cutoffHz, long? bitrateBps = 900_000) =>
        new("f", codec, 44100, 2, bitrateBps, 200, bandwidth, cutoffHz, "Ok", 0, 0, false, null);

    private static SuspectVerdict Aligned(double confidence) => new(SuspectLabeler.FromConfidence(confidence), confidence);

    // Build over what Candidates finds in the same files, as the scan will call it.
    private static ImposterResult Build(
        ScannedFile[] files, Func<string, string, SuspectVerdict?> alignmentOf,
        Func<string, string, double>? similarity = null, string[]? roots = null) =>
        ImposterSongs.Build(
            ImposterSongs.Candidates(files, Rips), files, Rips, roots ?? Checked, alignmentOf, similarity ?? NoSim, files.Length);

    // A path as these tests name a file: below /music/.
    private static string Short(string path) => path["/music/".Length..];

    private static string Names(IEnumerable<ScannedFile> files) => string.Join(", ", files.Select(f => Short(f.Path)));

    // Each copy that has a candidate, as "copy <- its references".
    private static string Show(ImposterCandidates found) =>
        string.Join(" | ", found.Copies.Select(c => $"{Short(c.Copy.Path)} <- {Names(c.References)}"));

    // Each song as "reference (verdict): copy grade, copy grade", with a copy's SameAudioAs after an equals sign.
    private static string Show(ImposterResult result) =>
        string.Join(" | ", result.Songs.Select(s => $"{Short(s.Reference.Path)} ({s.Verdict}): " + string.Join(", ", s.Copies.Select(c =>
            $"{Short(c.File.Path)} {c.Grade}" + (c.SameAudioAs is { } same ? $" = {Short(same)}" : "")))));

    // Each song's best copies, or "none" where nothing was ranked.
    private static string Winners(ImposterResult result) =>
        string.Join(" | ", result.Songs.Select(s => s.Best is { } best ? string.Join(", ", best.Winners.Select(Short)) : "none"));

    [Test]
    public async Task Candidates_ACopyNamedLikeAReferenceFile_GetsIt()
    {
        var found = ImposterSongs.Candidates(
            [F(Rips, "Artist Name - Song Title.m4a"), F(Old, "Artist Name - Song Title.flac")], Rips);

        await Assert.That(Show(found)).IsEqualTo("Old/Artist Name - Song Title.flac <- Rips/Artist Name - Song Title.m4a");
        await Assert.That(Names(found.NoReference)).IsEqualTo("");
        await Assert.That(Names(found.ReferenceFiles)).IsEqualTo("Rips/Artist Name - Song Title.m4a");
    }

    [Test]
    public async Task Candidates_AnAllArtistsName_FindsTheFirstArtistReference()
    {
        // The copy credits every artist and the reference only the first: the same title, with one
        // artist list inside the other.
        var found = ImposterSongs.Candidates(
            [F(Rips, "Artist Name - Song Title.m4a"), F(Old, "Artist Name; Guest Two - Song Title.flac")], Rips);

        await Assert.That(Show(found)).IsEqualTo("Old/Artist Name; Guest Two - Song Title.flac <- Rips/Artist Name - Song Title.m4a");
    }

    [Test]
    public async Task Candidates_BothNameRanksCountTogether()
    {
        // The identical name does not shut the collaboration out: two tracks can share a title and a
        // first artist, and the audio decides which one the copy is. The collaboration is listed
        // first, so the plain name coming first is the path order.
        var found = ImposterSongs.Candidates(
        [
            F(Rips, "Artist Name; Guest Two - Song Title.m4a"),
            F(Rips, "Artist Name - Song Title.m4a"),
            F(New, "Artist Name - Song Title.flac"),
        ], Rips);

        await Assert.That(Show(found)).IsEqualTo(
            "New/Artist Name - Song Title.flac <- Rips/Artist Name - Song Title.m4a, Rips/Artist Name; Guest Two - Song Title.m4a");
    }

    [Test]
    public async Task Candidates_AVersionWordApart_IsNoReference()
    {
        // The names twin once the version words are dropped, but a radio edit is not the extended mix.
        var found = ImposterSongs.Candidates(
            [F(Rips, "Artist Name - Song Title - Extended Mix.m4a"), F(Old, "Artist Name - Song Title - Radio Edit.flac")], Rips);

        await Assert.That(Show(found)).IsEqualTo("");
        await Assert.That(Names(found.NoReference)).IsEqualTo("Old/Artist Name - Song Title - Radio Edit.flac");
    }

    [Test]
    public async Task Candidates_TwoCheckedFolders_NeverPairWithEachOther()
    {
        var found = ImposterSongs.Candidates(
        [
            F(Rips, "Artist Name - Song Title.m4a"),
            F(Old, "Someone Else - Third Song.flac"),
            F(New, "Someone Else - Third Song.flac"),
        ], Rips);

        await Assert.That(Show(found)).IsEqualTo("");
        await Assert.That(Names(found.NoReference)).IsEqualTo("Old/Someone Else - Third Song.flac, New/Someone Else - Third Song.flac");
    }

    [Test]
    public async Task Candidates_TheReferenceRootIsMatchedIgnoringCase()
    {
        var found = ImposterSongs.Candidates(
            [F(Rips, "Artist Name - Song Title.m4a"), F(Old, "Artist Name - Song Title.flac")], "/MUSIC/RIPS");

        await Assert.That(Show(found)).IsEqualTo("Old/Artist Name - Song Title.flac <- Rips/Artist Name - Song Title.m4a");
        await Assert.That(Names(found.ReferenceFiles)).IsEqualTo("Rips/Artist Name - Song Title.m4a");
    }

    [Test]
    public async Task Grade_ASharedAudioGroup_Confirms()
    {
        await Assert.That(ImposterSongs.Grade(true, null, 200, 200)).IsEqualTo(CopyGrade.Confirmed);
        await Assert.That(ImposterSongs.Grade(true, Aligned(0.1), 200, 320)).IsEqualTo(CopyGrade.Confirmed);
    }

    [Test]
    public async Task Grade_LinesUpAndTheLengthsAgree_Confirms()
    {
        await Assert.That(ImposterSongs.Grade(false, Aligned(0.93), 200, 204)).IsEqualTo(CopyGrade.Confirmed);
        await Assert.That(ImposterSongs.Grade(false, Aligned(0.93), 200, 209.9)).IsEqualTo(CopyGrade.Confirmed);
    }

    [Test]
    public async Task Grade_LinesUpButTheLengthsDiffer_IsOtherLength()
    {
        await Assert.That(ImposterSongs.Grade(false, Aligned(0.93), 174, 424)).IsEqualTo(CopyGrade.OtherLength);
        await Assert.That(ImposterSongs.Grade(false, Aligned(0.93), 200, 210)).IsEqualTo(CopyGrade.OtherLength);
    }

    [Test]
    public async Task Grade_AnUnknownLength_IsNoGap()
    {
        await Assert.That(ImposterSongs.Grade(false, Aligned(0.93), null, 424)).IsEqualTo(CopyGrade.Confirmed);
    }

    [Test]
    public async Task Grade_FollowsTheAlignmentBands()
    {
        await Assert.That(ImposterSongs.Grade(false, Aligned(0.6), 200, 200)).IsEqualTo(CopyGrade.PartlySame);
        await Assert.That(ImposterSongs.Grade(false, Aligned(0.2), 200, 200)).IsEqualTo(CopyGrade.DoesntLineUp);
        await Assert.That(ImposterSongs.Grade(false, new SuspectVerdict(SuspectLabel.CantCompare, null), 200, 200))
            .IsEqualTo(CopyGrade.CantCompare);
        await Assert.That(ImposterSongs.Grade(false, null, 200, 200)).IsEqualTo(CopyGrade.CantCompare);
    }

    [Test]
    public async Task PairsToAlign_SkipsACopyWithAGroupMateAmongItsCandidates()
    {
        // Old's copy shares group 3 with the plain reference file, which settles it whatever the
        // collaboration would say. New's copy is in no group, so both its candidates are aligned.
        var pairs = ImposterSongs.PairsToAlign(ImposterSongs.Candidates(
        [
            F(Rips, "Artist Name - Song Title.m4a", 3),
            F(Rips, "Artist Name; Guest Two - Song Title.m4a"),
            F(Old, "Artist Name - Song Title.flac", 3),
            F(New, "Artist Name - Song Title.flac"),
        ], Rips));

        await Assert.That(string.Join(" | ", pairs.Select(p => $"{Short(p.Reference.Path)} ~ {Short(p.Copy.Path)}"))).IsEqualTo(
            "Rips/Artist Name - Song Title.m4a ~ New/Artist Name - Song Title.flac"
            + " | Rips/Artist Name; Guest Two - Song Title.m4a ~ New/Artist Name - Song Title.flac");
    }

    [Test]
    public async Task Build_ACopyInTheReferencesGroup_IsConfirmedWithoutAnAlignment()
    {
        var result = Build([F(Rips, "Artist Name - Song Title.m4a", 1), F(Old, "Artist Name - Song Title.flac", 1)], NoAlignment);

        await Assert.That(Show(result)).IsEqualTo("Rips/Artist Name - Song Title.m4a (AllConfirmed): Old/Artist Name - Song Title.flac Confirmed");
        await Assert.That(result.Songs[0].Copies[0].AlignConfidence).IsNull();
    }

    [Test]
    public async Task Build_TwoReferenceFilesUnderOneName_EachCopyJoinsTheOneItMatches()
    {
        // Each copy has both reference files as candidates; its audio group says which one it is.
        var plain = F(Rips, "Artist Name - Song Title.m4a", 1);
        var collab = F(Rips, "Artist Name; Guest Two - Song Title.m4a", 2);
        var oldCopy = F(Old, "Artist Name; Guest Two - Song Title.flac", 2);

        var apart = Build([plain, collab, oldCopy, F(New, "Artist Name - Song Title.flac", 1)], NoAlignment);

        await Assert.That(Show(apart)).IsEqualTo(
            "Rips/Artist Name - Song Title.m4a (AllConfirmed): New/Artist Name - Song Title.flac Confirmed"
            + " | Rips/Artist Name; Guest Two - Song Title.m4a (AllConfirmed): Old/Artist Name; Guest Two - Song Title.flac Confirmed");
        await Assert.That(Names(apart.ReferenceOnly)).IsEqualTo("");

        // New's plain-named copy holds the collaboration's audio, so it joins that song, not the identical name.
        var together = Build([plain, collab, oldCopy, F(New, "Artist Name - Song Title.flac", 2)], NoAlignment);

        await Assert.That(Show(together)).IsEqualTo(
            "Rips/Artist Name; Guest Two - Song Title.m4a (AllConfirmed): "
            + "Old/Artist Name; Guest Two - Song Title.flac Confirmed, New/Artist Name - Song Title.flac Confirmed");
        await Assert.That(Names(together.ReferenceOnly)).IsEqualTo("Rips/Artist Name - Song Title.m4a");
    }

    [Test]
    public async Task Build_WithNoGroup_KeepsTheBestGradedCandidate()
    {
        // The plain name comes first by path but does not line up; the collaboration does.
        var plain = F(Rips, "Artist Name - Song Title.m4a");
        var result = Build(
            [plain, F(Rips, "Artist Name; Guest Two - Song Title.m4a"), F(Old, "Artist Name - Song Title.flac")],
            (reference, _) => Aligned(reference == plain.Path ? 0.2 : 0.9));

        await Assert.That(Show(result)).IsEqualTo(
            "Rips/Artist Name; Guest Two - Song Title.m4a (AllConfirmed): Old/Artist Name - Song Title.flac Confirmed");
        await Assert.That(result.Songs[0].Copies[0].AlignConfidence).IsEqualTo(0.9);
        await Assert.That(Names(result.ReferenceOnly)).IsEqualTo("Rips/Artist Name - Song Title.m4a");
    }

    [Test]
    public async Task Build_TwoCandidatesShareTheGroup_TheMoreSimilarOneIsChosen()
    {
        // The reference folder holds the copy's recording under two names. The scan found the copy
        // closer to the collaboration, so it joins that one, though the plain name comes first by path.
        var collab = F(Rips, "Artist Name; Guest Two - Song Title.m4a", 1);
        var result = Build(
            [F(Rips, "Artist Name - Song Title.m4a", 1), collab, F(Old, "Artist Name - Song Title.flac", 1)],
            NoAlignment,
            (a, b) => a == collab.Path || b == collab.Path ? 0.99 : 0.9);

        await Assert.That(Show(result)).IsEqualTo(
            "Rips/Artist Name; Guest Two - Song Title.m4a (AllConfirmed): Old/Artist Name - Song Title.flac Confirmed");
    }

    [Test]
    public async Task Build_WithNoGroup_TheBetterGradeComesFirst_ThenTheHigherConfidence()
    {
        // The plain name lines up best but runs a minute longer, so it is only another length. Both
        // collaborations are confirmed, and the copy keeps the one that lines up better, though the
        // other comes first by path.
        var plain = F(Rips, "Artist Name - Song Title.m4a", seconds: 260);
        var three = F(Rips, "Artist Name; Guest Three - Song Title.m4a");
        var result = Build(
            [plain, three, F(Rips, "Artist Name; Guest Two - Song Title.m4a"), F(Old, "Artist Name - Song Title.flac")],
            (reference, _) => Aligned(reference == plain.Path ? 0.95 : reference == three.Path ? 0.85 : 0.9));

        await Assert.That(Show(result)).IsEqualTo(
            "Rips/Artist Name; Guest Two - Song Title.m4a (AllConfirmed): Old/Artist Name - Song Title.flac Confirmed");
        await Assert.That(result.Songs[0].Copies[0].AlignConfidence).IsEqualTo(0.9);
    }

    [Test]
    public async Task Build_Verdicts()
    {
        // Only the copies in no group are aligned, and neither of them lines up.
        var result = Build(
        [
            F(Rips, "Artist Name - Song Title.m4a", 1),
            F(Old, "Artist Name - Song Title.flac", 1),
            F(New, "Artist Name - Song Title.flac"),
            F(Rips, "Artist Name - Other Song.m4a"),
            F(Old, "Artist Name - Other Song.flac"),
            F(Rips, "Someone Else - Third Song.m4a", 3),
            F(Old, "Someone Else - Third Song.flac", 3),
            F(New, "Someone Else - Third Song.flac", 3),
        ], (_, _) => Aligned(0.2));

        await Assert.That(Show(result)).IsEqualTo(
            "Rips/Artist Name - Other Song.m4a (NoneConfirmed): Old/Artist Name - Other Song.flac DoesntLineUp"
            + " | Rips/Artist Name - Song Title.m4a (SomeConfirmed): "
            + "Old/Artist Name - Song Title.flac Confirmed, New/Artist Name - Song Title.flac DoesntLineUp"
            + " | Rips/Someone Else - Third Song.m4a (AllConfirmed): "
            + "Old/Someone Else - Third Song.flac Confirmed, New/Someone Else - Third Song.flac Confirmed");
    }

    [Test]
    public async Task Build_BestCopy_IsRankedAmongTheReferenceAndTheConfirmedCopies()
    {
        // New's copy is as good as Old's and comes first by path, but it does not line up with the
        // reference, so it is not ranked at all.
        var result = Build(
        [
            F(Rips, "Artist Name - Song Title.m4a", 1, row: Row("aac", "Lossy", 19_500, 256_000)),
            F(Old, "Artist Name - Song Title.flac", 1, row: Row("flac", "Lossless", null)),
            F(New, "Artist Name - Song Title.flac", row: Row("flac", "Lossless", null)),
        ], (_, _) => Aligned(0.2));

        await Assert.That(Winners(result)).IsEqualTo("Old/Artist Name - Song Title.flac");
    }

    [Test]
    public async Task Build_BestCopy_AnHonestLossyReferenceBeatsALosslessTranscode()
    {
        var result = Build(
        [
            F(Rips, "Artist Name - Song Title.m4a", 1, row: Row("aac", "Lossy", 19_500, 256_000)),
            F(Old, "Artist Name - Song Title.flac", 1, row: Row("flac", "Lossy", 16_000)),
        ], NoAlignment);

        await Assert.That(Winners(result)).IsEqualTo("Rips/Artist Name - Song Title.m4a");
    }

    [Test]
    public async Task Build_BestCopy_IdenticalTopCopiesTie()
    {
        var result = Build(
        [
            F(Rips, "Artist Name - Song Title.m4a", 1, row: Row("aac", "Lossy", 19_500, 256_000)),
            F(Old, "Artist Name - Song Title.flac", 1, row: Row("flac", "Lossless", null)),
            F(New, "Artist Name - Song Title.flac", 1, row: Row("flac", "Lossless", null)),
        ], NoAlignment, (a, b) => a.EndsWith(".flac") && b.EndsWith(".flac") ? 1 : 0);

        await Assert.That(Winners(result)).IsEqualTo("New/Artist Name - Song Title.flac, Old/Artist Name - Song Title.flac");
    }

    [Test]
    public async Task Build_NoConfirmedCopy_LeavesTheReferenceAsTheBestCopy()
    {
        var result = Build(
        [
            F(Rips, "Artist Name - Song Title.m4a", row: Row("aac", "Lossy", 19_500, 256_000)),
            F(Old, "Artist Name - Song Title.flac", row: Row("flac", "Lossless", null)),
        ], (_, _) => Aligned(0.2));

        await Assert.That(Winners(result)).IsEqualTo("Rips/Artist Name - Song Title.m4a");
    }

    [Test]
    public async Task Build_BestCopy_IsNullWhenNothingHasARow()
    {
        var result = Build([F(Rips, "Artist Name - Song Title.m4a"), F(Old, "Artist Name - Song Title.flac")], (_, _) => Aligned(0.2));

        await Assert.That(Show(result)).IsEqualTo("Rips/Artist Name - Song Title.m4a (NoneConfirmed): Old/Artist Name - Song Title.flac DoesntLineUp");
        await Assert.That(result.Songs[0].Best).IsNull();
    }

    [Test]
    public async Task Build_AnUnconfirmedCopy_NamesTheFileItsAudioMatches_AReferenceFileFirst()
    {
        // Old's copy holds the audio of another song of the reference folder, which a file in the
        // other checked folder shares too and comes first by path; the reference file is named. New's
        // copy is confirmed, so it names nothing although its group holds the reference file.
        var result = Build(
        [
            F(Rips, "Artist Name - Song Title.m4a", 1),
            F(Rips, "Artist Name - Other Song.m4a", 9),
            F(Old, "Artist Name - Song Title.flac", 9),
            F(New, "Someone Else - Third Song.flac", 9),
            F(New, "Artist Name - Song Title.flac", 1),
        ], (_, _) => Aligned(0.2));

        await Assert.That(Show(result)).IsEqualTo(
            "Rips/Artist Name - Song Title.m4a (SomeConfirmed): "
            + "Old/Artist Name - Song Title.flac DoesntLineUp = Rips/Artist Name - Other Song.m4a, New/Artist Name - Song Title.flac Confirmed");
    }

    [Test]
    public async Task Build_AnUnconfirmedCopy_NamesAFileInAnotherFolderFirst_AndNeverItself()
    {
        // No reference file shares the copy's audio. The file beside it comes first by path, yet the
        // one in the other checked folder is named.
        ScannedFile[] files =
        [
            F(Rips, "Artist Name - Song Title.m4a", 1),
            F(New, "Artist Name - Song Title.flac", 9),
            F(New, "Someone Else - Third Song.flac", 9),
            F(Old, "Artist Name - Other Song.flac", 9),
        ];

        await Assert.That(Show(Build(files, (_, _) => Aligned(0.2)))).IsEqualTo(
            "Rips/Artist Name - Song Title.m4a (NoneConfirmed): New/Artist Name - Song Title.flac DoesntLineUp = Old/Artist Name - Other Song.flac");

        // With only the file beside it left, the copy itself comes first by path but is never named.
        await Assert.That(Show(Build(files[..3], (_, _) => Aligned(0.2)))).IsEqualTo(
            "Rips/Artist Name - Song Title.m4a (NoneConfirmed): New/Artist Name - Song Title.flac DoesntLineUp = New/Someone Else - Third Song.flac");
    }

    [Test]
    public async Task Build_OrdersSongsByReferencePath_AndCopiesByFolderOrder()
    {
        // The song listed first sorts last, because the lower-case name comes first once case is
        // ignored. New's copies are listed first and come first by path, so only the folder order
        // puts Old's copy ahead of them.
        ScannedFile[] files =
        [
            F(Rips, "Someone Else - Third Song.m4a", 2),
            F(New, "Someone Else - Third Song.flac", 2),
            F(Rips, "artist name - song title.m4a", 1),
            F(New, "Artist Name - Song Title.flac", 1),
            F(Old, "Artist Name - Song Title.flac", 1),
        ];
        const string thirdSong = " | Rips/Someone Else - Third Song.m4a (AllConfirmed): New/Someone Else - Third Song.flac Confirmed";

        await Assert.That(Show(Build(files, NoAlignment, roots: [Old, New]))).IsEqualTo(
            "Rips/artist name - song title.m4a (AllConfirmed): "
            + "Old/Artist Name - Song Title.flac Confirmed, New/Artist Name - Song Title.flac Confirmed" + thirdSong);
        await Assert.That(Show(Build(files, NoAlignment, roots: [New, Old]))).IsEqualTo(
            "Rips/artist name - song title.m4a (AllConfirmed): "
            + "New/Artist Name - Song Title.flac Confirmed, Old/Artist Name - Song Title.flac Confirmed" + thirdSong);

        // A folder left out of the list goes last.
        await Assert.That(Show(Build(files, NoAlignment, roots: [Old]))).IsEqualTo(
            "Rips/artist name - song title.m4a (AllConfirmed): "
            + "Old/Artist Name - Song Title.flac Confirmed, New/Artist Name - Song Title.flac Confirmed" + thirdSong);
    }

    [Test]
    public async Task Build_AReferenceFolderThatNamesNothing_GivesNoSongs()
    {
        var result = Build(
        [
            F(Rips, "Someone Else - Third Song.m4a"),
            F(Old, "Artist Name - Song Title.flac"),
            F(New, "Artist Name - Song Title.flac"),
        ], NoAlignment);

        await Assert.That(Show(result)).IsEqualTo("");
        await Assert.That(Names(result.NoReference)).IsEqualTo("Old/Artist Name - Song Title.flac, New/Artist Name - Song Title.flac");
        await Assert.That(Names(result.ReferenceOnly)).IsEqualTo("Rips/Someone Else - Third Song.m4a");
    }
}
