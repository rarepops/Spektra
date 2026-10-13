using Spektra.Core;

namespace Spektra.Tests;

public sealed class FolderDiffViewTests
{
    private static readonly AuditRow Row =
        new("x.flac", "flac", 44100, 2, null, 180, "Lossless", null, "Ok", 0, 0, false, null);

    private static UnpairedFile Lone(string path, string root, long size = 1024, string? twin = null) =>
        new(path, root, Row, size) { TwinPath = twin };

    private static DupesGroupReport Group(string label, string tier, params string[] members) =>
        new(new DuplicateGroup(1, label, tier,
                [.. members.Select(m => new DuplicateMember(m, 0.99, tier, false))]),
            new QualityRanking([members[0]], "High", "reason", members),
            new Dictionary<string, AuditRow>(),
            new Dictionary<string, long>(),
            ReclaimableBytes: 0);

    private static string Labels(IEnumerable<DupesGroupReport> groups) =>
        string.Join("|", groups.Select(g => g.Group.Label));

    private static string Paths(IEnumerable<UnpairedFile> files) =>
        string.Join("|", files.Select(f => f.Path));

    [Test]
    public async Task VisibleGroups_OutsideADiff_KeepsScanOrderAndEveryTier()
    {
        // The scan hands groups over biggest reclaim first, and the duplicate
        // hunt reads them that way. Nothing may reorder or hide them here.
        DupesGroupReport[] all =
        [
            Group("Zebra", "High", @"A\z.flac", @"B\z.mp3"),
            Group("apple", "Medium", @"A\a.flac", @"B\a.mp3"),
        ];
        var shown = FolderDiffView.VisibleGroups(all, g => g, [], onlyDifferences: false);
        await Assert.That(Labels(shown)).IsEqualTo("Zebra|apple");
    }

    [Test]
    public async Task VisibleGroups_InADiff_SortsByLabelAndHidesTheConfidentOnes()
    {
        // Case must not decide the order, or "Zebra" sorts ahead of "apple".
        DupesGroupReport[] all =
        [
            Group("Zebra", "Medium", @"A\z.flac", @"B\z.mp3"),
            Group("Mango", "High", @"A\m.flac", @"B\m.mp3"),
            Group("apple", "Medium", @"A\a.flac", @"B\a.mp3"),
        ];
        var shown = FolderDiffView.VisibleGroups(all, g => g, [], onlyDifferences: true);
        await Assert.That(Labels(shown)).IsEqualTo("apple|Zebra");
    }

    [Test]
    public async Task VisibleGroups_AFilterMatchesTheLabelOrAnyMemberPath()
    {
        DupesGroupReport[] all =
        [
            Group("By Label 2019", "Medium", @"A\one.flac", @"B\one.mp3"),
            Group("By Path", "Medium", @"A\Live 2019\two.flac", @"B\two.mp3"),
            Group("Neither", "Medium", @"A\three.flac", @"B\three.mp3"),
        ];
        var shown = FolderDiffView.VisibleGroups(all, g => g, ["2019"], onlyDifferences: false);
        await Assert.That(Labels(shown)).IsEqualTo("By Label 2019|By Path");
    }

    [Test]
    public async Task Columns_OnePerRoot_InRootOrder_EvenWhenARootHasNothing()
    {
        // An empty column says "this folder has no extras", which is an
        // answer. A missing column would say the folder was never compared.
        // The roots are listed against the alphabet on purpose: columns
        // follow the order the folders were added in, not their names.
        string[] roots = [@"D:\MP3\Album", @"D:\FLAC\Album"];
        var columns = FolderDiffView.Columns(
            [Lone(@"D:\MP3\Album\extra.mp3", @"D:\MP3\Album")], roots, [], hideTitleTwins: true);

        await Assert.That(columns.Count).IsEqualTo(2);
        await Assert.That(columns[0].Root).IsEqualTo(@"D:\MP3\Album");
        await Assert.That(Paths(columns[0].Files)).IsEqualTo(@"D:\MP3\Album\extra.mp3");
        await Assert.That(columns[1].Root).IsEqualTo(@"D:\FLAC\Album");
        await Assert.That(columns[1].Files.Count).IsEqualTo(0);
        var labels = ScopeLabel.Distinguish(roots);
        await Assert.That(columns[0].Label).IsEqualTo(labels[0]);
        await Assert.That(columns[1].Label).IsEqualTo(labels[1]);
    }

    [Test]
    public async Task Columns_HidingNameMatches_DropsThemAndStillCountsThem()
    {
        UnpairedFile[] unpaired =
        [
            Lone(@"A\b.flac", "A", size: 300),
            Lone(@"A\a.flac", "A", size: 100),
            Lone(@"A\named.flac", "A", size: 200, twin: @"B\named.mp3"),
        ];
        var column = FolderDiffView.Columns(unpaired, ["A", "B"], [], hideTitleTwins: true)[0];

        await Assert.That(Paths(column.Files)).IsEqualTo(@"A\a.flac|A\b.flac");
        await Assert.That(column.Bytes).IsEqualTo(400L);
        await Assert.That(column.AlsoThere).IsEqualTo(1);
    }

    [Test]
    public async Task Columns_ShowingNameMatches_ListsThemAfterTheFilesOnlyHere()
    {
        // The name-matched file sorts first alphabetically, so only the rule
        // can put it last.
        UnpairedFile[] unpaired =
        [
            Lone(@"A\aaa.flac", "A", size: 200, twin: @"B\aaa.mp3"),
            Lone(@"A\zzz.flac", "A", size: 100),
        ];
        var column = FolderDiffView.Columns(unpaired, ["A", "B"], [], hideTitleTwins: false)[0];

        await Assert.That(Paths(column.Files)).IsEqualTo(@"A\zzz.flac|A\aaa.flac");
        // The size covers every file shown, the name-matched one too, while
        // the summary's count beside it covers only the files without a twin.
        // Pinned as the window behaves today; whether the size should follow
        // the count is undecided.
        await Assert.That(column.Bytes).IsEqualTo(300L);
        await Assert.That(column.AlsoThere).IsEqualTo(1);
    }

    [Test]
    public async Task Columns_OrderFilesByPath_IgnoringCase()
    {
        // Ordinal order would put every capital ahead of every lower-case
        // letter, and "Zebra" ahead of "apple".
        UnpairedFile[] unpaired = [Lone(@"A\Zebra.flac", "A"), Lone(@"A\apple.flac", "A")];
        var column = FolderDiffView.Columns(unpaired, ["A"], [], hideTitleTwins: true)[0];
        await Assert.That(Paths(column.Files)).IsEqualTo(@"A\apple.flac|A\Zebra.flac");
    }

    [Test]
    public async Task Columns_AFilterNarrowsByPath_IgnoringCase()
    {
        UnpairedFile[] unpaired =
        [
            Lone(@"A\Live at Leeds\one.flac", "A"),
            Lone(@"A\Studio\two.flac", "A"),
        ];
        var column = FolderDiffView.Columns(unpaired, ["A"], ["live"], hideTitleTwins: true)[0];
        await Assert.That(Paths(column.Files)).IsEqualTo(@"A\Live at Leeds\one.flac");
    }

    [Test]
    public async Task Columns_AFilterOfSeveralWords_NeedsEveryOneOfThem()
    {
        // Both paths hold "live"; only one holds "leeds" as well.
        UnpairedFile[] unpaired =
        [
            Lone(@"A\Live at Leeds\one.flac", "A"),
            Lone(@"A\Live at Budokan\two.flac", "A"),
        ];
        var column = FolderDiffView.Columns(unpaired, ["A"], ["live", "leeds"], hideTitleTwins: true)[0];
        await Assert.That(Paths(column.Files)).IsEqualTo(@"A\Live at Leeds\one.flac");
    }

    [Test]
    public async Task Columns_MatchAFileToItsRoot_IgnoringCase()
    {
        var column = FolderDiffView.Columns(
            [Lone(@"d:\flac\x.flac", @"d:\flac")], [@"D:\FLAC"], [], hideTitleTwins: true)[0];
        await Assert.That(column.Files.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Summary_SaysNothingOnlyHere_RatherThanShowingAZero()
    {
        await Assert.That(new DiffColumn("A", "A", [], 0, 0).Summary).IsEqualTo("nothing only here");
    }

    [Test]
    public async Task Summary_CountsOnlyTheFilesWithoutATwin()
    {
        UnpairedFile[] files = [Lone(@"A\a.flac", "A"), Lone(@"A\b.flac", "A")];
        await Assert.That(new DiffColumn("A", "A", files, 3L << 20, 0).Summary)
            .IsEqualTo("2 file(s) only here · 3.0 MB");
    }

    [Test]
    public async Task Summary_NamesTheHiddenCount_EvenWhenNothingIsLeft()
    {
        await Assert.That(new DiffColumn("A", "A", [], 0, 4).Summary)
            .IsEqualTo("nothing only here · 4 the other folder has under a matching name");
    }

    [Test]
    public async Task Summary_NamesBothNumbers()
    {
        UnpairedFile[] files = [Lone(@"A\a.flac", "A"), Lone(@"A\n.flac", "A", twin: @"B\n.mp3")];
        await Assert.That(new DiffColumn("A", "A", files, 1024, 2).Summary)
            .IsEqualTo("1 file(s) only here · 1.0 KB · 2 the other folder has under a matching name");
    }

    [Test]
    public async Task FooterSuffix_OrdinaryView_AddsNothing()
    {
        await Assert.That(FolderDiffView.FooterSuffix(
            onlyDifferences: false, filtered: false, sameHidden: 7, nameMatched: 5,
            inOneFolderOnly: 3, visibleGroups: 2, totalGroups: 9, notComparable: 1)).IsEqualTo("");
    }

    [Test]
    public async Task FooterSuffix_ADiff_LeavesOutTheCountsThatAreZero()
    {
        await Assert.That(FolderDiffView.FooterSuffix(
                onlyDifferences: true, filtered: false, sameHidden: 7, nameMatched: 0,
                inOneFolderOnly: 3, visibleGroups: 2, totalGroups: 9, notComparable: 0))
            .IsEqualTo(" · differences: 7 same hidden · 3 in one folder only · 2 weak match");
    }

    [Test]
    public async Task FooterSuffix_ADiff_NamesEveryCount_InOrderOfConfidence()
    {
        // Six different numbers, so a count wired to the wrong slot shows.
        await Assert.That(FolderDiffView.FooterSuffix(
                onlyDifferences: true, filtered: true, sameHidden: 7, nameMatched: 5,
                inOneFolderOnly: 3, visibleGroups: 2, totalGroups: 9, notComparable: 1))
            .IsEqualTo(
                " · differences: 7 same hidden · 5 same name only · 3 in one folder only"
                + " · 2 weak match · 1 not comparable · filter: 2 of 9 groups");
    }

    [Test]
    public async Task FooterSuffix_ADiff_NamesTheNameMatchesWithoutTheNotComparable()
    {
        await Assert.That(FolderDiffView.FooterSuffix(
                onlyDifferences: true, filtered: false, sameHidden: 7, nameMatched: 5,
                inOneFolderOnly: 3, visibleGroups: 2, totalGroups: 9, notComparable: 0))
            .IsEqualTo(" · differences: 7 same hidden · 5 same name only · 3 in one folder only · 2 weak match");
    }

    [Test]
    public async Task FooterSuffix_ADiff_NamesTheNotComparableWithoutTheNameMatches()
    {
        await Assert.That(FolderDiffView.FooterSuffix(
                onlyDifferences: true, filtered: false, sameHidden: 7, nameMatched: 0,
                inOneFolderOnly: 3, visibleGroups: 2, totalGroups: 9, notComparable: 1))
            .IsEqualTo(" · differences: 7 same hidden · 3 in one folder only · 2 weak match · 1 not comparable");
    }

    [Test]
    public async Task FooterSuffix_AFilterAlone_SaysHowManyGroupsItKept()
    {
        await Assert.That(FolderDiffView.FooterSuffix(
                onlyDifferences: false, filtered: true, sameHidden: 7, nameMatched: 5,
                inOneFolderOnly: 3, visibleGroups: 2, totalGroups: 9, notComparable: 1))
            .IsEqualTo(" · filter: 2 of 9 groups");
    }

    [Test]
    public async Task TwinNote_TheSameName_SaysTheAudioDiffers()
    {
        await Assert.That(FolderDiffView.TwinNote(Lone(@"A\Song.flac", "A", twin: @"B\SONG.FLAC")))
            .IsEqualTo("the other folder has this name too · the audio differs");
    }

    [Test]
    public async Task TwinNote_AnotherName_SaysWhichOne()
    {
        await Assert.That(FolderDiffView.TwinNote(Lone(@"A\Song.flac", "A", twin: @"B\sub\Song (feat. X).mp3")))
            .IsEqualTo("the other folder has: Song (feat. X).mp3");
    }

    [Test]
    public async Task TwinNote_NoTwin_IsNull()
    {
        await Assert.That(FolderDiffView.TwinNote(Lone(@"A\Song.flac", "A"))).IsNull();
    }

    [Test]
    [Arguments(@"D:\Lib\A\x.flac", @"D:\Lib", @"A\x.flac")]
    [Arguments(@"D:\Lib\A\x.flac", @"D:\Lib\", @"A\x.flac")]
    [Arguments(@"D:\Lib\A\x.flac", @"d:\lib", @"A\x.flac")]
    [Arguments(@"D:\Lib\A\x.flac", @"E:\Other", @"D:\Lib\A\x.flac")]
    [Arguments(@"D:\Lib\A\x.flac", "", @"D:\Lib\A\x.flac")]
    [Arguments("D:/Lib/A/x.flac", "D:/Lib", "A/x.flac")]
    public async Task RelativePath_DropsTheRootWhenTheFileIsUnderIt(string path, string root, string expected)
    {
        await Assert.That(FolderDiffView.RelativePath(Lone(path, root))).IsEqualTo(expected);
    }
}
