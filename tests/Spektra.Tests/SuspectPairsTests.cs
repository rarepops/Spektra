using Spektra.Core;

namespace Spektra.Tests;

public sealed class SuspectPairsTests
{
    private static readonly string[] AB = ["A", "B"];

    private static ScannedFile F(string path, int? group = null, double? seconds = 200) =>
        new(path, path[..path.IndexOf('\\')], group, seconds);

    private static string Show(IEnumerable<SuspectPair> pairs) =>
        string.Join(" | ", pairs.Select(p => $"{p.PathA} ~ {p.PathB} ({p.Strength})"));

    [Test]
    public async Task Both_sides_unpaired_with_one_name_is_a_suspect()
    {
        // The second length is unknown, as for a file that would not decode: the pair is still
        // listed, with whatever the scan did learn.
        var found = SuspectPairs.Find(
            [F(@"A\Artist Name - Song Title.flac", seconds: 424), F(@"B\Artist Name - Song Title.flac", seconds: null)], AB);

        await Assert.That(Show(found)).IsEqualTo(@"A\Artist Name - Song Title.flac ~ B\Artist Name - Song Title.flac (SameName)");
        await Assert.That(found[0].SecondsA).IsEqualTo(424.0);
        await Assert.That(found[0].SecondsB).IsNull();
        await Assert.That(found[0].SameAudioAsA).IsNull();
        await Assert.That(found[0].SameAudioAsB).IsNull();
    }

    [Test]
    public async Task One_side_grouped_with_a_third_file_is_a_suspect_that_names_it()
    {
        // B's copy is grouped with the first folder's all-artists copy of the track, while the plain
        // name under A matched nothing. That group-mate is only a credit match, which an identical
        // name outranks, so it cannot vouch for B; it is named so the reader sees where B's audio is.
        var found = SuspectPairs.Find(
        [
            F(@"A\Artist Name - Song Title.flac"),
            F(@"A\Artist Name; Guest Two - Song Title.flac", 7),
            F(@"B\Artist Name - Song Title.flac", 7),
        ], AB);

        await Assert.That(Show(found)).IsEqualTo(@"A\Artist Name - Song Title.flac ~ B\Artist Name - Song Title.flac (SameName)");
        await Assert.That(found[0].SameAudioAsB).IsEqualTo(@"A\Artist Name; Guest Two - Song Title.flac");
        await Assert.That(found[0].SameAudioAsA).IsNull();
    }

    [Test]
    public async Task Both_in_one_group_is_not_a_suspect()
    {
        // The rule reads the group id and nothing else, so a Medium group silences exactly as a High
        // one does: flagging Medium pairs would flag bit-identical files that sit in a Medium group
        // only because of a weaker third member.
        var found = SuspectPairs.Find(
            [F(@"A\Artist Name - Song Title.flac", 3), F(@"B\Artist Name - Song Title.flac", 3)], AB);

        await Assert.That(Show(found)).IsEqualTo("");
    }

    [Test]
    public async Task Twins_under_one_root_never_pair()
    {
        // Two copies in one folder are a duplicate, which is the ordinary view's business.
        var found = SuspectPairs.Find(
            [F(@"A\Artist Name - Song Title.flac"), F(@"A\Artist Name - Song Title.mp3")], AB);

        await Assert.That(Show(found)).IsEqualTo("");
    }

    [Test]
    public async Task Agreeing_radio_edits_and_extended_mixes_stay_quiet()
    {
        // TitleTwin drops the version words, so each edit's near-name twin over there is the other
        // edit, in another group. Only the closest twins are asked, and each edit's identical name
        // shares its group.
        var found = SuspectPairs.Find(
        [
            F(@"A\Artist Name - Song Title - Radio Edit.flac", 1),
            F(@"A\Artist Name - Song Title - Extended Mix.flac", 2),
            F(@"B\Artist Name - Song Title - Radio Edit.flac", 1),
            F(@"B\Artist Name - Song Title - Extended Mix.flac", 2),
        ], AB);

        await Assert.That(Show(found)).IsEqualTo("");
    }

    [Test]
    public async Task Repeated_album_names_stay_quiet()
    {
        // Each Intro has a same-name twin in the other album over there, in another group. Its own
        // counterpart shares its group, and that is enough.
        var found = SuspectPairs.Find(
        [
            F(@"A\Album One\01 - Intro.flac", 1),
            F(@"A\Album Two\01 - Intro.flac", 2),
            F(@"B\Album One\01 - Intro.flac", 1),
            F(@"B\Album Two\01 - Intro.flac", 2),
        ], AB);

        await Assert.That(Show(found)).IsEqualTo("");
    }

    [Test]
    public async Task A_crossed_swap_of_versions_is_flagged_twice()
    {
        // Both folders name the two edits the same, but the audio behind the names is swapped: the
        // identical names disagree, so both pairs are flagged.
        var found = SuspectPairs.Find(
        [
            F(@"A\Artist Name - Song Title - Radio Edit.flac", 1),
            F(@"A\Artist Name - Song Title - Extended Mix.flac", 2),
            F(@"B\Artist Name - Song Title - Radio Edit.flac", 2),
            F(@"B\Artist Name - Song Title - Extended Mix.flac", 1),
        ], AB);

        await Assert.That(Show(found)).IsEqualTo(
            @"A\Artist Name - Song Title - Extended Mix.flac ~ B\Artist Name - Song Title - Extended Mix.flac (SameName)"
            + @" | A\Artist Name - Song Title - Radio Edit.flac ~ B\Artist Name - Song Title - Radio Edit.flac (SameName)");
    }

    [Test]
    public async Task A_crossed_swap_of_artist_credits_is_flagged_twice()
    {
        // Each folder holds the real copy under one naming style and the fake under the other.
        // Identical names are asked before credit matches: asked together, each credit match would
        // vouch for the wrong file and the swap would pass unseen.
        var found = SuspectPairs.Find(
        [
            F(@"A\Artist Name - Song Title.flac", 1),
            F(@"A\Artist Name; Guest Two - Song Title.flac", 2),
            F(@"B\Artist Name - Song Title.flac", 2),
            F(@"B\Artist Name; Guest Two - Song Title.flac", 1),
        ], AB);

        await Assert.That(Show(found)).IsEqualTo(
            @"A\Artist Name - Song Title.flac ~ B\Artist Name - Song Title.flac (SameName)"
            + @" | A\Artist Name; Guest Two - Song Title.flac ~ B\Artist Name; Guest Two - Song Title.flac (SameName)");
    }

    [Test]
    public async Task The_twin_in_the_same_folder_is_shown_first()
    {
        // A\Live matched nothing and has two same-name twins under B. Choosing by path alone would
        // pair it with B\Bonus; the twin in the same folder below its root, B\Live, is the one shown.
        var found = SuspectPairs.Find(
        [
            F(@"A\Live\Artist Name - Song Title.flac"),
            F(@"A\Elsewhere\Artist Name - Song Title.flac", 6),
            F(@"B\Bonus\Artist Name - Song Title.flac", 5),
            F(@"B\Live\Artist Name - Song Title.flac", 6),
        ], AB);

        await Assert.That(Show(found)).IsEqualTo(
            @"A\Elsewhere\Artist Name - Song Title.flac ~ B\Bonus\Artist Name - Song Title.flac (SameName)"
            + @" | A\Live\Artist Name - Song Title.flac ~ B\Live\Artist Name - Song Title.flac (SameName)");
    }

    [Test]
    public async Task Folders_in_the_other_order_swap_the_sides()
    {
        // The new download given first: A and B swap, each with its own length, and nothing else changes.
        var found = SuspectPairs.Find(
            [F(@"A\Artist Name - Song Title.flac", seconds: 424), F(@"B\Artist Name - Song Title.flac", seconds: null)], ["B", "A"]);

        await Assert.That(Show(found)).IsEqualTo(@"B\Artist Name - Song Title.flac ~ A\Artist Name - Song Title.flac (SameName)");
        await Assert.That(found[0].SecondsA).IsNull();
        await Assert.That(found[0].SecondsB).IsEqualTo(424.0);
    }

    [Test]
    public async Task A_near_name_twin_counts_when_there_is_no_same_name_one()
    {
        // The Radio Edit twins the plain name only once the version word is dropped, and that is
        // all the other folder has, so it is the twin asked.
        var found = SuspectPairs.Find(
            [F(@"A\Artist Name - Song Title - Radio Edit.flac"), F(@"B\Artist Name - Song Title.flac", 4)], AB);

        await Assert.That(Show(found)).IsEqualTo(@"A\Artist Name - Song Title - Radio Edit.flac ~ B\Artist Name - Song Title.flac (NearName)");
    }

    [Test]
    public async Task Each_other_root_is_judged_on_its_own()
    {
        // A and B agree, so neither is a suspect against the other; C disagrees with both.
        var found = SuspectPairs.Find(
        [
            F(@"A\Song Title Here.flac", 1),
            F(@"B\Song Title Here.flac", 1),
            F(@"C\Song Title Here.flac", 2),
        ], ["A", "B", "C"]);

        await Assert.That(Show(found)).IsEqualTo(
            @"A\Song Title Here.flac ~ C\Song Title Here.flac (SameName)"
            + @" | B\Song Title Here.flac ~ C\Song Title Here.flac (SameName)");
    }
}
