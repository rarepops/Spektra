using Spektra.Core;

namespace Spektra.Tests;

public sealed class ImposterReportTests
{
    private const string Rips = "/music/Rips", Old = "/music/Old", New = "/music/New";
    private static readonly string[] Checked = [Old, New];

    private static AuditRow Flac(double seconds) =>
        new("x.flac", "flac", 44100, 2, null, seconds, "Lossless", null, "Ok", 0, 0, false, null);

    // An honest aac whose content stops at 19.5 kHz.
    private static AuditRow Aac(double seconds) =>
        new("x.m4a", "aac", 44100, 2, 256_000, seconds, "Lossy", 19_500, "Ok", 0, 0, false, null);

    // A file as the scan reports it: its length is its audit row's, so a file the scan could not
    // read has neither.
    private static ScannedFile F(string root, string name, AuditRow? row) =>
        new($"{root}/{name}", root, null, row?.DurationSeconds) { Row = row };

    // A song whose every copy is the reference recording: flac copies of an aac reference file, so
    // the copies tie for the best copy.
    private static ImposterSong AllClear(string name, params string[] roots)
    {
        var reference = F(Rips, $"{name}.m4a", Aac(200));
        var copies = roots.Select(root => F(root, $"{name}.flac", Flac(200))).ToList();
        return new ImposterSong(
            reference, [.. copies.Select(c => new GradedCopy(c, CopyGrade.Confirmed, null, null))], SongVerdict.AllConfirmed,
            new QualityRanking([.. copies.Select(c => c.Path)], "High", "reason", [.. copies.Select(c => c.Path), reference.Path]));
    }

    private static readonly ScannedFile ThreeReference = F(Rips, "Artist Name - Song Three.m4a", Aac(221));
    private static readonly ScannedFile OneReference = F(Rips, "Artist Name - Song One.m4a", Aac(221));
    private static readonly ScannedFile OneOld = F(Old, "Artist Name - Song One.flac", Flac(221));
    private static readonly ScannedFile BonusTrack = F(Old, "Bonus Track.flac", null);
    private static readonly ScannedFile RipsOnly = F(Rips, "Rips Only.m4a", Aac(200));

    // No copy confirmed: the one copy holds the audio of another song of the reference folder, and
    // the reference file, ranked alone, is the best copy.
    private static readonly ImposterSong SongThree = new(
        ThreeReference,
        [new GradedCopy(F(Old, "Artist Name - Song Three.flac", Flac(192)), CopyGrade.DoesntLineUp, 0.04321, $"{Rips}/Artist Name - Other Song.m4a")],
        SongVerdict.NoneConfirmed,
        new QualityRanking([ThreeReference.Path], "High", "identical copies, keep either", [ThreeReference.Path]));

    // One copy confirmed and the best copy, the other only partly the same.
    private static readonly ImposterSong SongOne = new(
        OneReference,
        [
            new GradedCopy(OneOld, CopyGrade.Confirmed, null, null),
            new GradedCopy(F(New, "Artist Name - Song One.flac", Flac(219)), CopyGrade.PartlySame, 0.61268, null),
        ],
        SongVerdict.SomeConfirmed,
        new QualityRanking(
            [OneOld.Path], "Medium", "winner is full-band lossless (flac), runner-up is aac cut at 19.5 kHz", [OneOld.Path, OneReference.Path]));

    // The result the documented text describes, its songs out of display order on purpose.
    private static readonly ImposterResult Documented = new(
        Rips, Checked,
        [
            AllClear("Artist Name - Song Two", Old, New),
            SongOne,
            AllClear("Artist Name - Other Song", New),
            SongThree,
            AllClear("Artist Name - Song Four", Old),
        ],
        [BonusTrack], [RipsOnly], 14);

    private static string Lines(params string[] lines) => string.Concat(lines.Select(line => line + "\n"));

    [Test]
    public async Task Rows_ListEverySong_ReferenceThenCopies_ThenTheTwoLists()
    {
        var rows = ImposterReport.Rows(Documented);

        // The text's order, the all-clear songs included (by name, after the two the text lists),
        // each reference row followed by its copies; then the two lists, which name no song.
        await Assert.That(string.Join(" ", rows.Select(r => r.Kind))).IsEqualTo(
            "reference copy reference copy copy reference copy reference copy reference copy copy no-reference reference-only");
        await Assert.That(string.Join(" | ", rows.Select(r => r.Song ?? "-").Distinct())).IsEqualTo(
            "Artist Name - Song Three | Artist Name - Song One | Artist Name - Other Song | Artist Name - Song Four | Artist Name - Song Two | -");
        await Assert.That(string.Join(" ", rows.Skip(5).Take(7).Select(r => r.Verdict).Distinct())).IsEqualTo("AllConfirmed");

        // The best copy of each song: the reference file where no copy is confirmed, the winning
        // copy otherwise, and both copies of a tie.
        await Assert.That(string.Concat(rows.Select(r => r.Best ? '*' : '.'))).IsEqualTo("*..*..*.*.**..");

        // Every column of the rows that show each kind of value. Grade, ReferencePath,
        // ReferenceSeconds and SameAudioAs belong to copy rows, AlignConfidence is rounded to 3
        // places, and Quality is null for a file the scan could not read.
        await Assert.That(rows[0]).IsEqualTo(new ImposterRow(
            "reference", "Artist Name - Song Three", "NoneConfirmed", Rips, "/music/Rips/Artist Name - Song Three.m4a",
            null, null, 221, null, null, true, "aac cut at 19.5 kHz", null));
        await Assert.That(rows[1]).IsEqualTo(new ImposterRow(
            "copy", "Artist Name - Song Three", "NoneConfirmed", Old, "/music/Old/Artist Name - Song Three.flac",
            "DoesntLineUp", 0.043, 192, "/music/Rips/Artist Name - Song Three.m4a", 221, false, "full-band lossless (flac)",
            "/music/Rips/Artist Name - Other Song.m4a"));
        await Assert.That(rows[3]).IsEqualTo(new ImposterRow(
            "copy", "Artist Name - Song One", "SomeConfirmed", Old, "/music/Old/Artist Name - Song One.flac",
            "Confirmed", null, 221, "/music/Rips/Artist Name - Song One.m4a", 221, true, "full-band lossless (flac)", null));
        await Assert.That(rows[4].AlignConfidence).IsEqualTo(0.613);
        await Assert.That(rows[12]).IsEqualTo(new ImposterRow(
            "no-reference", null, null, Old, "/music/Old/Bonus Track.flac", null, null, null, null, null, false, null, null));
        await Assert.That(rows[13]).IsEqualTo(new ImposterRow(
            "reference-only", null, null, Rips, "/music/Rips/Rips Only.m4a", null, null, 200, null, null, false, "aac cut at 19.5 kHz", null));
    }

    [Test]
    public async Task Rows_SerializeWithTheDocumentedColumns()
    {
        var csv = Reporting.ToCsv(ImposterReport.Rows(Documented));

        await Assert.That(csv[..csv.IndexOf('\n')]).IsEqualTo(
            "Kind,Song,Verdict,Root,Path,Grade,AlignConfidence,Seconds,ReferencePath,ReferenceSeconds,Best,Quality,SameAudioAs");
    }

    [Test]
    public async Task Text_MatchesTheDocumentedShape()
    {
        // Every line ends in \n, the last one included.
        await Assert.That(ImposterReport.Text(Documented)).IsEqualTo(Lines(
            "Imposter Buster · reference Rips · 5 song(s) · 1 with no copy confirmed · 1 with a copy that differs · 3 all clear · 1 file(s) with no reference · 1 reference file(s) not in the checked folders · 14 scanned",
            "No copy is the reference recording · 1",
            "  1. Artist Name - Song Three",
            "     reference: /music/Rips/Artist Name - Song Three.m4a · 3:41 · aac cut at 19.5 kHz",
            "     Old: /music/Old/Artist Name - Song Three.flac · 3:12 · Doesn't line up: a cover, another song, or another master",
            "          the same audio as Artist Name - Other Song.m4a in Rips",
            "     best copy: the reference file",
            "A copy differs from the reference recording · 1",
            "  2. Artist Name - Song One",
            "     reference: /music/Rips/Artist Name - Song One.m4a · 3:41 · aac cut at 19.5 kHz",
            "     Old: /music/Old/Artist Name - Song One.flac · 3:41 · The reference recording",
            "     New: /music/New/Artist Name - Song One.flac · 3:39 · Partly the same: another mix or edit",
            "     best copy: Old: Artist Name - Song One.flac · winner is full-band lossless (flac), runner-up is aac cut at 19.5 kHz",
            "No reference by name · 1 file(s)",
            "     /music/Old/Bonus Track.flac",
            "Not in the checked folders · 1 reference file(s)",
            "     /music/Rips/Rips Only.m4a"));
    }

    [Test]
    public async Task Text_AReferenceThatNamesNothing_SaysSo()
    {
        // No checked file carries a reference file's name, so there is no song to list.
        var result = new ImposterResult(Rips, Checked, [], [BonusTrack, F(New, "Another Track.flac", Flac(200))], [RipsOnly], 3);

        await Assert.That(ImposterReport.Text(result)).IsEqualTo(Lines(
            "Imposter Buster · reference Rips · 0 song(s) · 0 with no copy confirmed · 0 with a copy that differs · 0 all clear · 2 file(s) with no reference · 1 reference file(s) not in the checked folders · 3 scanned",
            "No reference by name · 2 file(s)",
            "     /music/Old/Bonus Track.flac",
            "     /music/New/Another Track.flac",
            "Not in the checked folders · 1 reference file(s)",
            "     /music/Rips/Rips Only.m4a"));
    }

    [Test]
    public async Task Text_AFileWithNoRow_PrintsItsPathAndLengthOnly()
    {
        // The scan could not read the reference file, so it has no quality to show and no length,
        // and nothing could be compared with it. A copy's line still ends in its grade, the copy
        // the scan could not read included.
        var reference = F(Rips, "Artist Name - Broken Song.m4a", null);
        var song = new ImposterSong(
            reference,
            [
                new GradedCopy(F(Old, "Artist Name - Broken Song.flac", Flac(200)), CopyGrade.CantCompare, null, null),
                new GradedCopy(F(New, "Artist Name - Broken Song.flac", null), CopyGrade.CantCompare, null, null),
            ],
            SongVerdict.NoneConfirmed, null);

        await Assert.That(ImposterReport.Text(new ImposterResult(Rips, Checked, [song], [], [], 3))).IsEqualTo(Lines(
            "Imposter Buster · reference Rips · 1 song(s) · 1 with no copy confirmed · 0 with a copy that differs · 0 all clear · 0 file(s) with no reference · 0 reference file(s) not in the checked folders · 3 scanned",
            "No copy is the reference recording · 1",
            "  1. Artist Name - Broken Song",
            "     reference: /music/Rips/Artist Name - Broken Song.m4a · ?",
            "     Old: /music/Old/Artist Name - Broken Song.flac · 3:20 · Can't compare: a copy is broken or too short",
            "     New: /music/New/Artist Name - Broken Song.flac · ? · Can't compare: a copy is broken or too short",
            "     best copy: no copy could be ranked"));
    }

    [Test]
    public async Task HasFindings_OnlyWhenACopyIsNotConfirmed()
    {
        // A file with no reference and a reference file with no copy say nothing about a copy.
        var lists = new ImposterResult(Rips, Checked, [], [BonusTrack], [RipsOnly], 2);
        await Assert.That(ImposterReport.HasFindings(lists)).IsFalse();
        await Assert.That(ImposterReport.HasFindings(lists with { Songs = [AllClear("Artist Name - Song Two", Old, New)] })).IsFalse();

        // Any grade but Confirmed is a finding, the mildest included, and so is one beside a confirmed copy.
        ImposterResult OneCopy(CopyGrade grade) => lists with
        {
            Songs =
            [
                new ImposterSong(
                    OneReference, [new GradedCopy(OneOld, grade, null, null)],
                    grade == CopyGrade.Confirmed ? SongVerdict.AllConfirmed : SongVerdict.NoneConfirmed, null),
            ],
        };
        await Assert.That(string.Join(" ", Enum.GetValues<CopyGrade>().Select(g => $"{g}={ImposterReport.HasFindings(OneCopy(g))}")))
            .IsEqualTo("Confirmed=False OtherLength=True PartlySame=True DoesntLineUp=True CantCompare=True");
        await Assert.That(ImposterReport.HasFindings(lists with { Songs = [SongOne] })).IsTrue();
    }
}
