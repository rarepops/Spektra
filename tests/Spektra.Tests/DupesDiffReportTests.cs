using Spektra.Core;

namespace Spektra.Tests;

public sealed class DupesDiffReportTests
{
    private static readonly string[] Roots = ["A", "B"];

    private static readonly SuspectPair Cover = new(
        @"A\Shared Title.flac", "A", 424, null,
        @"B\Shared Title.flac", "B", 174, @"A\Other Name.flac", NameStrength.SameName);

    private static readonly SuspectPair Broken = new(
        @"A\Broken Song.flac", "A", null, null,
        @"B\Broken Song.flac", "B", 200, null, NameStrength.SameName);

    private static readonly SuspectPair Edit = new(
        @"A\Song Title - Radio Edit.flac", "A", 200, null,
        @"B\Song Title.flac", "B", 215, null, NameStrength.NearName);

    // Out of display order on purpose: the report lists them in the window's order.
    private static readonly SuspectPair[] Pairs = [Edit, Broken, Cover];

    private static readonly SuspectVerdict[] Verdicts =
    [
        new(SuspectLabel.PartlySame, 0.6),
        new(SuspectLabel.CantCompare, null),
        new(SuspectLabel.DoesntLineUp, 0.17213),
    ];

    private static UnpairedFile Lone(string path, string root, double seconds, long size = 1024) =>
        new(path, root, new AuditRow("x.flac", "flac", 44100, 2, null, seconds, "Lossless", null, "Ok", 0, 0, false, null), size);

    private static DupesGroupReport Group(int id, string label, string tier, params string[] members) =>
        new(new DuplicateGroup(id, label, tier, [.. members.Select(m => new DuplicateMember(m, 0.9, tier, false))]),
            new QualityRanking([members[0]], "High", "reason", members),
            new Dictionary<string, AuditRow>(),
            new Dictionary<string, long>(),
            ReclaimableBytes: 0);

    private static DupesDiffRow OneFile(string kind, string root, string path) =>
        new(kind, root, path, null, null, null, null, null, null, null, null, null, null, null);

    // A scan of the folders A and B. B\Shared Title.flac is the same recording as
    // A\Other Name.flac, so it is grouped and not unpaired; A\Broken Song.flac could
    // not be analyzed.
    private static DupesResult Result(params SuspectPair[] suspects) => new(
        [
            Group(2, "weak one", "Medium", @"A\w.flac", @"B\w.mp3"),
            // Confidently the same recording, so the diff never mentions it.
            Group(3, "Other Name", "High", @"A\Other Name.flac", @"B\Shared Title.flac"),
        ],
        [
            new NotAnalyzedFile(@"A\Broken Song.flac", "ffmpeg failed to decode this file."),
            new NotAnalyzedFile(@"A\Jingle.wav", "shorter than 20 s"),
        ],
        FilesScanned: 11, ReclaimableBytes: 0)
    {
        Unpaired =
        [
            Lone(@"A\Only Here.flac", "A", 180, size: 1_048_576),
            Lone(@"A\Shared Title.flac", "A", 424),
            Lone(@"A\Song Title - Radio Edit.flac", "A", 200),
            Lone(@"B\Broken Song.flac", "B", 200),
            Lone(@"B\Song Title.flac", "B", 215),
        ],
        Suspects = suspects,
    };

    [Test]
    public async Task Text_MirrorsTheDiffView()
    {
        string[] expected =
        [
            "Same name, different audio · 2 to listen to first · 1 similar",
            "  1. Doesn't line up: a cover, another song, or a heavy remaster · same name · 7:04 vs 2:54",
            @"     A: A\Shared Title.flac",
            @"     B: B\Shared Title.flac",
            "     the copy in B is the same audio as Other Name.flac in A",
            "  2. Can't compare: one copy is broken or too short · same name · ? vs 3:20",
            @"     A: A\Broken Song.flac",
            @"     B: B\Broken Song.flac",
            "  3. Partly the same: another mix or edit · near name · 3:20 vs 3:35",
            @"     A: A\Song Title - Radio Edit.flac",
            @"     B: B\Song Title.flac",
            "Weak matches · 1",
            "  weak one · 2 files · sameness Medium",
            @"     A\w.flac",
            @"     B\w.mp3",
            "Only in A · 1 file(s) · 1.0 MB",
            @"     A\Only Here.flac",
            "Only in B · nothing",
            "1 file(s) not analyzed",
            @"     A\Jingle.wav (shorter than 20 s)",
        ];

        // Every line ends in \n, the last one included.
        await Assert.That(DupesDiffReport.Text(Result(Pairs), Verdicts, Roots))
            .IsEqualTo(string.Concat(expected.Select(line => line + "\n")));
    }

    [Test]
    public async Task Rows_ListEachKindOnce_InOrder()
    {
        var rows = DupesDiffReport.Rows(Result(Pairs), Verdicts, Roots);

        await Assert.That(string.Join(" ", rows.Select(r => r.Kind)))
            .IsEqualTo("suspect suspect suspect weak weak only-in not-analyzed");
        await Assert.That(rows[0].Label).IsEqualTo("DoesntLineUp");
        await Assert.That(rows[0].AlignConfidence).IsEqualTo(0.172);
        await Assert.That(rows[0].OtherSameAudioAs).IsEqualTo(@"A\Other Name.flac");
        await Assert.That(rows[2].NameStrength).IsEqualTo("NearName");
        await Assert.That(rows[3].Group).IsEqualTo(2);
        await Assert.That(rows[4].Group).IsEqualTo(2);
        await Assert.That(rows[5].Path).IsEqualTo(@"A\Only Here.flac");
        await Assert.That(rows[5].Seconds).IsEqualTo(180);
        // A\Broken Song.flac could not be analyzed either, but its pair already lists it.
        await Assert.That(rows[6].Path).IsEqualTo(@"A\Jingle.wav");
        await Assert.That(rows[6].Reason).IsEqualTo("shorter than 20 s");
    }

    [Test]
    public async Task Rows_AWeakMemberInASuspectPair_IsBothSuspectAndWeak()
    {
        // B\w.mp3 matched A\w.flac only weakly, and another copy of that name in A
        // matched it not at all: both facts hold, so it is listed under both kinds.
        var fourth = new SuspectPair(
            @"A\Live\w.flac", "A", 181, null,
            @"B\w.mp3", "B", 180, @"A\w.flac", NameStrength.SameName);
        var rows = DupesDiffReport.Rows(
            Result([.. Pairs, fourth]), [.. Verdicts, new SuspectVerdict(SuspectLabel.DoesntLineUp, 0.05)], Roots);

        var listed = rows.Where(r => r.Path == @"B\w.mp3" || r.OtherPath == @"B\w.mp3").Select(r => r.Kind);
        await Assert.That(string.Join(" ", listed)).IsEqualTo("suspect weak");
    }

    [Test]
    public async Task Differs_IsFalseWhenOnlyUnanalyzedFilesRemain()
    {
        // A file the scan could not read says nothing either way about the folders.
        DupesDiffRow[] unanalyzed =
        [
            OneFile("not-analyzed", "A", @"A\Jingle.wav"),
            OneFile("not-analyzed", "B", @"B\Intro.wav"),
        ];
        await Assert.That(DupesDiffReport.Differs(unanalyzed)).IsFalse();
        await Assert.That(DupesDiffReport.Differs([.. unanalyzed, OneFile("only-in", "A", @"A\Only Here.flac")])).IsTrue();
    }
}
