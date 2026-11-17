using Spektra.Core;

namespace Spektra.Tests;

public sealed class ImposterViewTests
{
    private const string Rips = "/music/Rips", Old = "/music/Old", New = "/music/New";
    private static readonly string[] Checked = [Old, New];

    private static ScannedFile F(string root, string name, int? group = null, double? seconds = 200) =>
        new($"{root}/{name}", root, group, seconds);

    // A song built by hand: each copy with no alignment confidence and no other file named for its
    // audio, and no ranking (a test that needs one sets Best).
    private static ImposterSong Song(
        ScannedFile reference, SongVerdict verdict, params (ScannedFile File, CopyGrade Grade)[] copies) =>
        new(reference, [.. copies.Select(c => new GradedCopy(c.File, c.Grade, null, null))], verdict, null);

    // The name of each song Ordered keeps, in its order.
    private static string Order(IEnumerable<ImposterSong> songs, params string[] tokens) =>
        string.Join(" | ", ImposterView.Ordered(songs, tokens).Select(s => Path.GetFileNameWithoutExtension(s.Reference.Path)));

    [Test]
    public async Task GradeText_ShortGrade_VerdictText_GradeLimit_HoldTheExactCopy()
    {
        await Assert.That(ImposterView.GradeText(CopyGrade.Confirmed)).IsEqualTo("The reference recording");
        await Assert.That(ImposterView.GradeText(CopyGrade.SameByFingerprint))
            .IsEqualTo("Same by fingerprint, but the openings don't line up: another master, an edit, or a re-recording");
        await Assert.That(ImposterView.GradeText(CopyGrade.OtherLength))
            .IsEqualTo("Same opening, another length: an edit, another cut, or a cut-off file");
        await Assert.That(ImposterView.GradeText(CopyGrade.PartlySame)).IsEqualTo("Partly the same: another mix or edit");
        await Assert.That(ImposterView.GradeText(CopyGrade.DoesntLineUp))
            .IsEqualTo("Doesn't line up: a cover, another song, or another master");
        await Assert.That(ImposterView.GradeText(CopyGrade.CantCompare)).IsEqualTo("Can't compare: a copy is broken or too short");

        await Assert.That(ImposterView.ShortGrade(CopyGrade.Confirmed)).IsEqualTo("reference recording");
        await Assert.That(ImposterView.ShortGrade(CopyGrade.SameByFingerprint)).IsEqualTo("same by fingerprint");
        await Assert.That(ImposterView.ShortGrade(CopyGrade.OtherLength)).IsEqualTo("other length");
        await Assert.That(ImposterView.ShortGrade(CopyGrade.PartlySame)).IsEqualTo("partly the same");
        await Assert.That(ImposterView.ShortGrade(CopyGrade.DoesntLineUp)).IsEqualTo("doesn't line up");
        await Assert.That(ImposterView.ShortGrade(CopyGrade.CantCompare)).IsEqualTo("can't compare");

        await Assert.That(ImposterView.VerdictText(SongVerdict.AllConfirmed)).IsEqualTo("Every copy is confirmed as the reference recording");
        await Assert.That(ImposterView.VerdictText(SongVerdict.SomeConfirmed)).IsEqualTo("A copy is not confirmed as the reference recording");
        await Assert.That(ImposterView.VerdictText(SongVerdict.NoneConfirmed)).IsEqualTo("No copy is confirmed as the reference recording");

        await Assert.That(ImposterView.GradeLimit).IsEqualTo(
            "A grade describes evidence, never a verdict on the file. A copy is confirmed when its fingerprint is all but identical to the reference file's, or when their first 10 s line up and their lengths agree. A fingerprint that only matches says the same song and arrangement, which another master and a careful re-recording both give. Another intro or a larger offset also reads as doesn't line up.");
    }

    [Test]
    public async Task NoTextCallsAFileFakeOrAnImposter()
    {
        // The wording describes the evidence and never calls a file fake: that is the listener's call.
        string[] texts =
        [
            .. Enum.GetValues<CopyGrade>().Select(ImposterView.GradeText),
            .. Enum.GetValues<CopyGrade>().Select(ImposterView.ShortGrade),
            .. Enum.GetValues<SongVerdict>().Select(ImposterView.VerdictText),
            .. Enum.GetValues<SongVerdict>().Select(ImposterView.ShortVerdict),
            ImposterView.GradeLimit,
        ];

        // Every text that holds either word, which must be none.
        var offending = texts.Select(t => t.ToLowerInvariant()).Where(t => t.Contains("fake") || t.Contains("imposter"));
        await Assert.That(string.Join(" | ", offending)).IsEqualTo("");
    }

    [Test]
    public async Task AnUnknownEnumValue_Throws()
    {
        // A grade or verdict added later is worded and placed on purpose, never shown or sorted unseen.
        await Assert.That(() => ImposterView.GradeText((CopyGrade)99)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => ImposterView.ShortGrade((CopyGrade)99)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => ImposterView.VerdictText((SongVerdict)99)).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => ImposterView.ShortVerdict((SongVerdict)99)).ThrowsExactly<ArgumentOutOfRangeException>();

        // Ordered places each verdict and each grade by name too.
        var song = Song(F(Rips, "Alpha.m4a"), SongVerdict.NoneConfirmed, (F(Old, "Alpha.flac"), CopyGrade.DoesntLineUp));
        var unknownGrade = Song(F(Rips, "Bravo.m4a"), SongVerdict.NoneConfirmed, (F(Old, "Bravo.flac"), (CopyGrade)99));
        await Assert.That(() => ImposterView.Ordered([song, unknownGrade], [])).ThrowsExactly<ArgumentOutOfRangeException>();
        await Assert.That(() => ImposterView.Ordered([song, song with { Verdict = (SongVerdict)99 }], []))
            .ThrowsExactly<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Ordered_PutsTheSongsWithNoConfirmedCopyFirst_ThenByGrade_ThenByName()
    {
        // Down to Charlie the names run against the order, so only the verdicts and grades put them in
        // place. Echo has a copy that doesn't line up, but a confirmed one too, so it follows every
        // song with no confirmed copy. Golf's first copy is only partly the same, but its other can't
        // compare, and the most worrying copy counts. Delta's copy is another length, which comes
        // before Charlie's, the same by fingerprint. alpha and Bravo differ only in name, and a sort
        // that told capitals from small letters would put Bravo first.
        ImposterSong[] songs =
        [
            Song(F(Rips, "Bravo.m4a"), SongVerdict.AllConfirmed, (F(Old, "Bravo.flac"), CopyGrade.Confirmed)),
            Song(F(Rips, "Charlie.m4a"), SongVerdict.NoneConfirmed, (F(Old, "Charlie.flac"), CopyGrade.SameByFingerprint)),
            Song(
                F(Rips, "Echo.m4a"), SongVerdict.SomeConfirmed,
                (F(Old, "Echo.flac"), CopyGrade.Confirmed), (F(New, "Echo.flac"), CopyGrade.DoesntLineUp)),
            Song(F(Rips, "Foxtrot.m4a"), SongVerdict.NoneConfirmed, (F(Old, "Foxtrot.flac"), CopyGrade.PartlySame)),
            Song(F(Rips, "alpha.m4a"), SongVerdict.AllConfirmed, (F(Old, "alpha.flac"), CopyGrade.Confirmed)),
            Song(F(Rips, "Hotel.m4a"), SongVerdict.NoneConfirmed, (F(Old, "Hotel.flac"), CopyGrade.DoesntLineUp)),
            Song(F(Rips, "Delta.m4a"), SongVerdict.NoneConfirmed, (F(Old, "Delta.flac"), CopyGrade.OtherLength)),
            Song(
                F(Rips, "Golf.m4a"), SongVerdict.NoneConfirmed,
                (F(Old, "Golf.flac"), CopyGrade.PartlySame), (F(New, "Golf.flac"), CopyGrade.CantCompare)),
        ];

        await Assert.That(Order(songs)).IsEqualTo("Hotel | Golf | Foxtrot | Delta | Charlie | Echo | alpha | Bravo");
    }

    [Test]
    public async Task Ordered_KeepsASongWhoseNameOrAnyPathHoldsEveryWord()
    {
        ImposterSong[] songs =
        [
            Song(F(Rips, "Alpha.m4a"), SongVerdict.NoneConfirmed, (F(Old, "Alpha.flac"), CopyGrade.DoesntLineUp)),
            Song(F(Rips, "Bravo.m4a"), SongVerdict.NoneConfirmed, (F(New, "Bravo.flac"), CopyGrade.DoesntLineUp)),
        ];

        // Only Alpha's copy sits in Old.
        await Assert.That(Order(songs, "old")).IsEqualTo("Alpha");
        // Both reference files sit in Rips, but only Alpha holds Old as well, on another of its files.
        await Assert.That(Order(songs, "rips", "old")).IsEqualTo("Alpha");
        await Assert.That(Order(songs, "charlie")).IsEqualTo("");
    }

    [Test]
    public async Task Summary_CountsEveryKind()
    {
        var none = Song(F(Rips, "Alpha.m4a"), SongVerdict.NoneConfirmed);
        var some = Song(F(Rips, "Bravo.m4a"), SongVerdict.SomeConfirmed);
        var all = Song(F(Rips, "Charlie.m4a"), SongVerdict.AllConfirmed);
        var noReference = F(Old, "Delta.flac");
        var referenceOnly = F(Rips, "Echo.m4a");

        var result = new ImposterResult(
            Rips, Checked, [all, none, all, some, all], [noReference, F(New, "Foxtrot.flac")], [referenceOnly], 14);
        await Assert.That(ImposterView.Summary(result)).IsEqualTo(
            "5 song(s) · 1 with no copy confirmed · 1 with a copy that differs · 3 all clear · 2 file(s) with no reference · 1 reference file(s) not in the checked folders · 14 scanned");

        // Every count differs from the others, so no two can trade places unseen.
        var counts = result with
        {
            Songs = [none, none, some, some, some, all, all, all, all],
            NoReference = [.. Enumerable.Repeat(noReference, 5)],
            ReferenceOnly = [.. Enumerable.Repeat(referenceOnly, 6)],
            FilesScanned = 1234,
        };
        await Assert.That(ImposterView.Summary(counts)).IsEqualTo(
            "9 song(s) · 2 with no copy confirmed · 3 with a copy that differs · 4 all clear · 5 file(s) with no reference · 6 reference file(s) not in the checked folders · 1234 scanned");
    }

    [Test]
    public async Task BestText_NamesTheWinner()
    {
        var reference = F(Rips, "Artist Name - Song Title.m4a", 1);
        var oldCopy = F(Old, "Artist Name - Song Title.flac", 1);
        var newCopy = F(New, "Artist Name - Song Title.flac", 1);
        var song = Song(reference, SongVerdict.AllConfirmed, (oldCopy, CopyGrade.Confirmed), (newCopy, CopyGrade.Confirmed));

        // Ranked alone, the ranker reads "identical copies, keep either", which would be wrong here.
        var alone = new QualityRanking([reference.Path], "High", "identical copies, keep either", [reference.Path]);
        await Assert.That(ImposterView.BestText(song with { Best = alone }, Rips, Checked)).IsEqualTo("the reference file");

        const string reason = "winner is full-band lossless (flac), runner-up is aac cut at 19.5 kHz";
        var copyWins = new QualityRanking([oldCopy.Path], "Medium", reason, [oldCopy.Path, reference.Path]);
        await Assert.That(ImposterView.BestText(song with { Best = copyWins }, Rips, Checked))
            .IsEqualTo($"Old: Artist Name - Song Title.flac · {reason}");

        const string tie = "2 identical top copies (keep either); runner-up is aac cut at 19.5 kHz";
        var tied = new QualityRanking([newCopy.Path, oldCopy.Path], "High", tie, [newCopy.Path, oldCopy.Path, reference.Path]);
        await Assert.That(ImposterView.BestText(song with { Best = tied }, Rips, Checked))
            .IsEqualTo($"New: Artist Name - Song Title.flac = Old: Artist Name - Song Title.flac · {tie}");

        await Assert.That(ImposterView.BestText(song, Rips, Checked)).IsEqualTo("no copy could be ranked");
    }

    [Test]
    public async Task SameAudioNote_NamesTheFileAndItsFolder()
    {
        // Old's copy holds the audio of another song of the reference folder.
        var copy = new GradedCopy(
            F(Old, "Artist Name - Song Title.flac", 9), CopyGrade.DoesntLineUp, 0.2, $"{Rips}/Artist Name - Other Song.m4a");

        await Assert.That(ImposterView.SameAudioNote(copy, Rips, Checked)).IsEqualTo("the same audio as Artist Name - Other Song.m4a in Rips");
        await Assert.That(ImposterView.SameAudioNote(copy with { SameAudioAs = null }, Rips, Checked)).IsNull();
    }

    [Test]
    public async Task Playlist_ListsEachUnconfirmedCopyThenItsReference()
    {
        // Whole seconds, rounded (424.4 down, a half up, 12.6 up), and -1 where the length is unknown.
        // Third Song is all clear, so it is left out and takes no number. Song Title's copy in New is
        // in group 8 like Other Song's: only a copy already listed for the same song is left out.
        var titleReference = F(Rips, "Artist Name - Song Title.m4a", 1, 212.5);
        var titleNew = F(New, "Artist Name - Song Title.flac", 8, 424.4);
        var otherReference = F(Rips, "Artist Name - Other Song.m4a", seconds: null);
        var otherOld = F(Old, "Artist Name - Other Song.flac", 7, 12.6);
        var otherNew = F(New, "Artist Name - Other Song.flac", 8);

        var text = ImposterView.Playlist(
        [
            Song(
                titleReference, SongVerdict.SomeConfirmed,
                (F(Old, "Artist Name - Song Title.flac", 1), CopyGrade.Confirmed), (titleNew, CopyGrade.DoesntLineUp)),
            Song(
                F(Rips, "Someone Else - Third Song.m4a", 3), SongVerdict.AllConfirmed,
                (F(Old, "Someone Else - Third Song.flac", 3), CopyGrade.Confirmed)),
            Song(otherReference, SongVerdict.NoneConfirmed, (otherOld, CopyGrade.CantCompare), (otherNew, CopyGrade.PartlySame)),
        ], Rips, Checked);

        await Assert.That(text).IsEqualTo(
            "#EXTM3U\n" +
            $"#EXTINF:424,1 New · doesn't line up · Artist Name - Song Title\n{Path.GetFullPath(titleNew.Path)}\n" +
            $"#EXTINF:213,1 reference · Artist Name - Song Title\n{Path.GetFullPath(titleReference.Path)}\n" +
            $"#EXTINF:13,2 Old · can't compare · Artist Name - Other Song\n{Path.GetFullPath(otherOld.Path)}\n" +
            $"#EXTINF:200,2 New · partly the same · Artist Name - Other Song\n{Path.GetFullPath(otherNew.Path)}\n" +
            $"#EXTINF:-1,2 reference · Artist Name - Other Song\n{Path.GetFullPath(otherReference.Path)}\n");
    }

    [Test]
    public async Task Playlist_SkipsACopyHoldingTheSameAudioAsOneAlreadyListed()
    {
        // Both copies of Song Title are in group 5: the same audio twice, listed once. Other Song's
        // copies are in no group, which matches them with nothing, so both are listed.
        var titleReference = F(Rips, "Artist Name - Song Title.m4a");
        var titleOld = F(Old, "Artist Name - Song Title.flac", 5);
        var otherReference = F(Rips, "Artist Name - Other Song.m4a");
        var otherOld = F(Old, "Artist Name - Other Song.flac");
        var otherNew = F(New, "Artist Name - Other Song.flac");

        var text = ImposterView.Playlist(
        [
            Song(
                titleReference, SongVerdict.NoneConfirmed,
                (titleOld, CopyGrade.DoesntLineUp), (F(New, "Artist Name - Song Title.flac", 5), CopyGrade.DoesntLineUp)),
            Song(otherReference, SongVerdict.NoneConfirmed, (otherOld, CopyGrade.DoesntLineUp), (otherNew, CopyGrade.DoesntLineUp)),
        ], Rips, Checked);

        await Assert.That(text).IsEqualTo(
            "#EXTM3U\n" +
            $"#EXTINF:200,1 Old · doesn't line up · Artist Name - Song Title\n{Path.GetFullPath(titleOld.Path)}\n" +
            $"#EXTINF:200,1 reference · Artist Name - Song Title\n{Path.GetFullPath(titleReference.Path)}\n" +
            $"#EXTINF:200,2 Old · doesn't line up · Artist Name - Other Song\n{Path.GetFullPath(otherOld.Path)}\n" +
            $"#EXTINF:200,2 New · doesn't line up · Artist Name - Other Song\n{Path.GetFullPath(otherNew.Path)}\n" +
            $"#EXTINF:200,2 reference · Artist Name - Other Song\n{Path.GetFullPath(otherReference.Path)}\n");
    }

    [Test]
    public async Task Playlist_KeepsNonAsciiPathsExactly()
    {
        var reference = F(Rips, "Artist Name - How Much Is It？ Café.m4a");
        var copy = F(Old, "Artist Name - How Much Is It？ Café.flac");

        var text = ImposterView.Playlist([Song(reference, SongVerdict.NoneConfirmed, (copy, CopyGrade.DoesntLineUp))], Rips, Checked);

        await Assert.That(text).Contains($"\n{Path.GetFullPath(copy.Path)}\n");
    }

    [Test]
    public async Task ShortVerdict_HoldsTheExactCopy()
    {
        await Assert.That(ImposterView.ShortVerdict(SongVerdict.NoneConfirmed)).IsEqualTo("No copy confirmed");
        await Assert.That(ImposterView.ShortVerdict(SongVerdict.SomeConfirmed)).IsEqualTo("A copy differs");
        await Assert.That(ImposterView.ShortVerdict(SongVerdict.AllConfirmed)).IsEqualTo("All clear");
    }

    [Test]
    public async Task BestCopiesList_HoldsOnePathPerSongInSongOrder()
    {
        // The songs in no sorted order, which the list keeps. Charlie's two confirmed copies tie and
        // the ranker put New's first, so only New's is listed. Alpha's confirmed copy wins, Echo has
        // nothing ranked and no line, the reference file is Delta's best copy, and Foxtrot's name
        // holds ？ and é, kept exactly.
        static ImposterSong Ranked(ImposterSong song, params string[] winners) =>
            song with { Best = new QualityRanking(winners, "High", "", winners) };
        var charlie = Ranked(
            Song(
                F(Rips, "Charlie.m4a", 3), SongVerdict.AllConfirmed,
                (F(Old, "Charlie.flac", 3), CopyGrade.Confirmed), (F(New, "Charlie.flac", 3), CopyGrade.Confirmed)),
            $"{New}/Charlie.flac", $"{Old}/Charlie.flac");
        var alpha = Ranked(
            Song(
                F(Rips, "Alpha.m4a", 1), SongVerdict.SomeConfirmed,
                (F(Old, "Alpha.flac", 1), CopyGrade.Confirmed), (F(New, "Alpha.flac"), CopyGrade.DoesntLineUp)),
            $"{Old}/Alpha.flac");
        var echo = Song(F(Rips, "Echo.m4a"), SongVerdict.NoneConfirmed, (F(Old, "Echo.flac"), CopyGrade.CantCompare));
        var delta = Ranked(
            Song(F(Rips, "Delta.m4a"), SongVerdict.NoneConfirmed, (F(Old, "Delta.flac"), CopyGrade.DoesntLineUp)),
            $"{Rips}/Delta.m4a");
        var foxtrot = Ranked(
            Song(
                F(Rips, "Artist Name - How Much Is It？ Café.m4a", 6), SongVerdict.AllConfirmed,
                (F(Old, "Artist Name - How Much Is It？ Café.flac", 6), CopyGrade.Confirmed)),
            $"{Old}/Artist Name - How Much Is It？ Café.flac");
        var result = new ImposterResult(Rips, Checked, [charlie, alpha, echo, delta, foxtrot], [], [], 11);

        await Assert.That(ImposterView.BestCopiesList(result)).IsEqualTo(
            "/music/New/Charlie.flac\n/music/Old/Alpha.flac\n/music/Rips/Delta.m4a\n/music/Old/Artist Name - How Much Is It？ Café.flac\n");
        // With no song ranked there is no line at all, not even an empty one.
        await Assert.That(ImposterView.BestCopiesList(result with { Songs = [echo] })).IsEqualTo("");
    }
}
