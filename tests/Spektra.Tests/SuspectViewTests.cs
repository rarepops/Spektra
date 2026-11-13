using System.Text;
using Spektra.Core;

namespace Spektra.Tests;

public sealed class SuspectViewTests
{
    private const string Title = "Same name, different audio";

    private static readonly string[] AB = ["A", "B"];

    private static SuspectVerdict V(SuspectLabel label) => new(label, null);

    // A pair built directly: its roots are A and B.
    private static SuspectPair Pair(
        string a, string b, NameStrength strength = NameStrength.SameName,
        double? secondsA = 200, double? secondsB = 200,
        string? sameAudioAsA = null, string? sameAudioAsB = null) =>
        new(a, "A", secondsA, sameAudioAsA, b, "B", secondsB, sameAudioAsB, strength);

    // A row for the file `name` (a folder may lead it) in both roots.
    private static (SuspectPair Pair, SuspectVerdict? Verdict) Row(
        string name, SuspectLabel? label, NameStrength strength = NameStrength.SameName) =>
        (Pair($@"A\{name}.flac", $@"B\{name}.flac", strength), label is { } l ? V(l) : null);

    // The PathA of each row Visible keeps, in its order.
    private static string Order(
        IEnumerable<(SuspectPair Pair, SuspectVerdict? Verdict)> rows, params string[] tokens) =>
        string.Join(" | ", SuspectView.Visible(rows, r => r.Pair, r => r.Verdict, tokens).Select(r => r.Pair.PathA));

    [Test]
    public async Task Visible_PutsTheTopTierFirst_DoesntLineUpBeforeCantCompare()
    {
        // The names run against the label order, so a sort on the name alone would reverse it, and the
        // near-name row sorts before the same-name row by name, so only the strength puts it after.
        var rows = new[]
        {
            Row("Bravo", SuspectLabel.PartlySame),
            Row("Echo", SuspectLabel.DoesntLineUp),
            Row("Alpha", SuspectLabel.SamePerformance),
            Row("Charlie", SuspectLabel.CantCompare),
            Row("Delta", SuspectLabel.DoesntLineUp, NameStrength.NearName),
        };

        await Assert.That(Order(rows)).IsEqualTo(
            @"A\Echo.flac | A\Delta.flac | A\Charlie.flac | A\Bravo.flac | A\Alpha.flac");
    }

    [Test]
    public async Task Visible_WithinALabel_OrdersByName_IgnoringCase()
    {
        // The folders run against the names, so only the file name decides. A sort that told capitals
        // from small letters would put Cherry before banana.
        var rows = new[]
        {
            Row(@"Album 1\banana", SuspectLabel.DoesntLineUp),
            Row(@"Album 3\Cherry", SuspectLabel.DoesntLineUp),
            Row(@"Album 2\Apple", SuspectLabel.DoesntLineUp),
        };

        await Assert.That(Order(rows)).IsEqualTo(
            @"A\Album 2\Apple.flac | A\Album 1\banana.flac | A\Album 3\Cherry.flac");
    }

    [Test]
    public async Task Visible_TheSameFileNameInTwoFolders_FallsBackToThePath()
    {
        var rows = new[]
        {
            Row(@"Album 2\Intro", SuspectLabel.DoesntLineUp),
            Row(@"Album 1\Intro", SuspectLabel.DoesntLineUp),
        };

        await Assert.That(Order(rows)).IsEqualTo(@"A\Album 1\Intro.flac | A\Album 2\Intro.flac");
    }

    [Test]
    public async Task Visible_RowsStillBeingChecked_ComeBeforeEveryLabel()
    {
        // Bravo has no label yet and sorts after Alpha by name: only its rank puts it first.
        var rows = new[]
        {
            Row("Alpha", SuspectLabel.DoesntLineUp),
            Row("Bravo", null),
        };

        await Assert.That(Order(rows)).IsEqualTo(@"A\Bravo.flac | A\Alpha.flac");
    }

    [Test]
    public async Task Visible_KeepsRowsWhoseEitherPathHoldsEveryWord()
    {
        var rows = new (SuspectPair Pair, SuspectVerdict? Verdict)[]
        {
            // One word in each path: between them the pair holds both.
            (Pair(@"A\Alpha (Live).flac", @"B\Alpha 2019.flac"), V(SuspectLabel.DoesntLineUp)),
            // Both words in one path.
            (Pair(@"A\Bravo (Live) 2019.flac", @"B\Bravo.flac"), V(SuspectLabel.DoesntLineUp)),
            // One of the words only, though both paths hold it.
            (Pair(@"A\Charlie (Live).flac", @"B\Charlie (Live).flac"), V(SuspectLabel.DoesntLineUp)),
        };

        await Assert.That(Order(rows, "live", "2019")).IsEqualTo(@"A\Alpha (Live).flac | A\Bravo (Live) 2019.flac");
    }

    [Test]
    public async Task Heading_CountsEachTier_AndWhatIsLeft()
    {
        await Assert.That(SuspectView.Heading(0, 0, 0, false)).IsEqualTo($"{Title} · none");
        await Assert.That(SuspectView.Heading(2, 9, 0, false)).IsEqualTo($"{Title} · 2 to listen to first · 9 similar");
        await Assert.That(SuspectView.Heading(0, 0, 3, true)).IsEqualTo($"{Title} · 0 to listen to first · 0 similar · 3 checking…");
        await Assert.That(SuspectView.Heading(1, 1, 2, false)).IsEqualTo($"{Title} · 1 to listen to first · 1 similar · 2 not checked");

        // Nothing left to check: no suffix, whether or not a check is running.
        await Assert.That(SuspectView.Heading(2, 9, 0, true)).IsEqualTo($"{Title} · 2 to listen to first · 9 similar");
        await Assert.That(SuspectView.Heading(0, 0, 0, true)).IsEqualTo($"{Title} · none");
    }

    [Test]
    public async Task Lengths_ShowsBoth_AndAGapIsTenSecondsOrMore()
    {
        static SuspectPair Of(double? a, double? b) => Pair(@"A\x.flac", @"B\x.flac", secondsA: a, secondsB: b);

        await Assert.That(SuspectView.Lengths(Of(424, 174))).IsEqualTo("7:04 vs 2:54");
        await Assert.That(SuspectView.HasLengthGap(Of(424, 174))).IsTrue();

        await Assert.That(SuspectView.Lengths(Of(null, 174))).IsEqualTo("? vs 2:54");
        await Assert.That(SuspectView.HasLengthGap(Of(null, 174))).IsFalse();
        await Assert.That(SuspectView.Lengths(Of(174, null))).IsEqualTo("2:54 vs ?");
        await Assert.That(SuspectView.HasLengthGap(Of(174, null))).IsFalse();

        await Assert.That(SuspectView.HasLengthGap(Of(200, 209.9))).IsFalse();
        await Assert.That(SuspectView.HasLengthGap(Of(200, 210))).IsTrue();
        await Assert.That(SuspectView.HasLengthGap(Of(210, 200))).IsTrue();
    }

    [Test]
    public async Task CrossMatch_NamesTheOtherCopyAndItsFolder()
    {
        const string guest = "Artist Name; Guest Two - Song Title.flac";
        const string a = @"A\Artist Name - Song Title.flac";
        const string b = @"B\Artist Name - Song Title.flac";

        await Assert.That(SuspectView.CrossMatch(Pair(a, b, sameAudioAsB: @"A\" + guest), AB))
            .IsEqualTo($"the copy in B is the same audio as {guest} in A");

        await Assert.That(SuspectView.CrossMatch(Pair(a, b, sameAudioAsA: @"B\x.flac", sameAudioAsB: @"A\" + guest), AB))
            .IsEqualTo($"the copy in A is the same audio as x.flac in B · the copy in B is the same audio as {guest} in A");

        await Assert.That(SuspectView.CrossMatch(Pair(a, b), AB)).IsNull();
    }

    [Test]
    public async Task CrossMatch_LabelsFoldersByWhatTellsThemApart_AnUnlistedRootAsItself()
    {
        // Two folders that end alike are told apart by their parents, as the columns do.
        var oldRoot = Path.GetFullPath(Path.Combine("Music", "Old", "Pop"));
        var newRoot = Path.GetFullPath(Path.Combine("Music", "New", "Pop"));
        string[] roots = [oldRoot, newRoot];
        var oldLabel = Path.Combine("Old", "Pop");
        var newLabel = Path.Combine("New", "Pop");
        var pair = new SuspectPair(
            Path.Combine(oldRoot, "Song.flac"), oldRoot, 200, null,
            Path.Combine(newRoot, "Song.flac"), newRoot, 200, Path.Combine(oldRoot, "Other.flac"),
            NameStrength.SameName);

        await Assert.That(SuspectView.CrossMatch(pair, roots))
            .IsEqualTo($"the copy in {newLabel} is the same audio as Other.flac in {oldLabel}");

        // A root that is not in the list stands for itself.
        var outside = pair with { RootA = "Elsewhere", SameAudioAsA = Path.Combine(newRoot, "x.flac"), SameAudioAsB = null };
        await Assert.That(SuspectView.CrossMatch(outside, roots))
            .IsEqualTo($"the copy in Elsewhere is the same audio as x.flac in {newLabel}");
    }

    [Test]
    public async Task LabelText_DescribesTheEvidence_NeverAVerdict()
    {
        await Assert.That(SuspectView.LabelText(SuspectLabel.DoesntLineUp))
            .IsEqualTo("Doesn't line up: a cover, another song, or a heavy remaster");
        await Assert.That(SuspectView.LabelText(SuspectLabel.CantCompare))
            .IsEqualTo("Can't compare: one copy is broken or too short");
        await Assert.That(SuspectView.LabelText(SuspectLabel.PartlySame))
            .IsEqualTo("Partly the same: another mix or edit");
        await Assert.That(SuspectView.LabelText(SuspectLabel.SamePerformance))
            .IsEqualTo("Same performance (another master, or identical)");

        await Assert.That(SuspectView.ShortLabel(SuspectLabel.DoesntLineUp)).IsEqualTo("doesn't line up");
        await Assert.That(SuspectView.ShortLabel(SuspectLabel.CantCompare)).IsEqualTo("can't compare");
        await Assert.That(SuspectView.ShortLabel(SuspectLabel.PartlySame)).IsEqualTo("partly the same");
        await Assert.That(SuspectView.ShortLabel(SuspectLabel.SamePerformance)).IsEqualTo("same performance");

        // The wording describes the evidence and never calls a copy fake: that is the listener's call.
        foreach (var label in Enum.GetValues<SuspectLabel>())
        {
            foreach (var text in new[] { SuspectView.LabelText(label), SuspectView.ShortLabel(label) })
            {
                await Assert.That(text.Contains("fake", StringComparison.OrdinalIgnoreCase)).IsFalse();
                await Assert.That(text.Contains("imposter", StringComparison.OrdinalIgnoreCase)).IsFalse();
            }
        }
    }

    [Test]
    public async Task IsTopTier_IsDoesntLineUpAndCantCompareOnly()
    {
        await Assert.That(SuspectView.IsTopTier(SuspectLabel.DoesntLineUp)).IsTrue();
        await Assert.That(SuspectView.IsTopTier(SuspectLabel.CantCompare)).IsTrue();
        await Assert.That(SuspectView.IsTopTier(SuspectLabel.PartlySame)).IsFalse();
        await Assert.That(SuspectView.IsTopTier(SuspectLabel.SamePerformance)).IsFalse();
    }

    [Test]
    public async Task StrengthText_SaysHowCloselyTheNamesAgree()
    {
        await Assert.That(SuspectView.StrengthText(NameStrength.SameName)).IsEqualTo("same name");
        await Assert.That(SuspectView.StrengthText(NameStrength.NearName)).IsEqualTo("near name");
    }

    [Test]
    public async Task Playlist_ListsTopTierPairs_AThenB_InTheOrderGiven()
    {
        static string Full(string folder, string file) => Path.GetFullPath(Path.Combine("Music", folder, file));
        static SuspectPair Of(string file, double? secondsA, double? secondsB) =>
            new(Full("A", file), "A", secondsA, null, Full("B", file), "B", secondsB, null, NameStrength.SameName);

        // Whole seconds, rounded (424.4 down, 12.6 up), and -1 where the length is unknown.
        var text = SuspectView.Playlist(
            [
                (Of("Artist Name - Song Title.flac", 424.4, 174), V(SuspectLabel.DoesntLineUp)),
                (Of("Other Name.flac", 200, 200), V(SuspectLabel.PartlySame)),
                (Of("Third Name.flac", null, 12.6), V(SuspectLabel.CantCompare)),
            ], AB);

        var songA = Full("A", "Artist Name - Song Title.flac");
        var songB = Full("B", "Artist Name - Song Title.flac");
        var thirdA = Full("A", "Third Name.flac");
        var thirdB = Full("B", "Third Name.flac");
        await Assert.That(text).IsEqualTo(
            "#EXTM3U\n" +
            $"#EXTINF:424,1 A · doesn't line up · Artist Name - Song Title\n{songA}\n" +
            $"#EXTINF:174,1 B · doesn't line up · Artist Name - Song Title\n{songB}\n" +
            $"#EXTINF:-1,2 A · can't compare · Third Name\n{thirdA}\n" +
            $"#EXTINF:13,2 B · can't compare · Third Name\n{thirdB}\n");
    }

    [Test]
    public async Task Playlist_LabelsEachCopyByItsFolder_AndSkipsPairsWithoutAVerdict()
    {
        var oldRoot = Path.GetFullPath(Path.Combine("Music", "Old", "Pop"));
        var newRoot = Path.GetFullPath(Path.Combine("Music", "New", "Pop"));
        SuspectPair Of(string file) => new(
            Path.Combine(oldRoot, file), oldRoot, 200, null, Path.Combine(newRoot, file), newRoot, 200, null, NameStrength.SameName);

        // The unlabelled pair is not a first-tier pair just because the default label would be.
        var text = SuspectView.Playlist(
            [(Of("Unchecked.flac"), null), (Of("Song One.flac"), V(SuspectLabel.DoesntLineUp))], [oldRoot, newRoot]);

        await Assert.That(text).IsEqualTo(
            "#EXTM3U\n" +
            $"#EXTINF:200,1 {Path.Combine("Old", "Pop")} · doesn't line up · Song One\n{Path.Combine(oldRoot, "Song One.flac")}\n" +
            $"#EXTINF:200,1 {Path.Combine("New", "Pop")} · doesn't line up · Song One\n{Path.Combine(newRoot, "Song One.flac")}\n");
    }

    [Test]
    public async Task Playlist_WritesFullPaths_EvenForARelativeOne()
    {
        // A playlist resolves a relative entry from its own folder, not from where the scan ran.
        var a = Path.Combine("Music", "A", "Song.flac");
        var b = Path.Combine("Music", "B", "Song.flac");
        var pair = new SuspectPair(a, "A", 200, null, b, "B", 200, null, NameStrength.SameName);

        var text = SuspectView.Playlist([(pair, V(SuspectLabel.CantCompare))], AB);

        await Assert.That(text).IsEqualTo(
            "#EXTM3U\n" +
            $"#EXTINF:200,1 A · can't compare · Song\n{Path.GetFullPath(a)}\n" +
            $"#EXTINF:200,1 B · can't compare · Song\n{Path.GetFullPath(b)}\n");
    }

    [Test]
    public async Task Playlist_KeepsNonAsciiPathsExact()
    {
        var a = Path.GetFullPath(Path.Combine("Music", "A", "Artist Name - How Much Is It？ Café.flac"));
        var b = Path.GetFullPath(Path.Combine("Music", "B", "Artist Name - How Much Is It？ Café.flac"));
        var pair = new SuspectPair(a, "A", 200, null, b, "B", 200, null, NameStrength.SameName);

        var text = SuspectView.Playlist([(pair, V(SuspectLabel.DoesntLineUp))], AB);

        await Assert.That(text.Contains(a)).IsTrue();
        await Assert.That(text.Contains(b)).IsTrue();

        // A writer using UTF-8 without a byte-order mark starts the file at the header.
        var bytes = Encoding.UTF8.GetBytes(text);
        var header = Encoding.UTF8.GetBytes("#EXTM3U");
        await Assert.That(bytes.Take(header.Length).SequenceEqual(header)).IsTrue();
    }
}
