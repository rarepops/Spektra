using Spektra.Core;

namespace Spektra.Tests;

public sealed class TitleTwinTests
{
    private static string Words(string path) =>
        string.Join(" ", TitleTwin.Words(path).Order(StringComparer.Ordinal));

    [Test]
    [Arguments(@"D:\M\Guano Apes - Open Your Eyes.flac", "apes eyes guano open your")]
    // The extension goes, and so does the punctuation between words: the two
    // sides of a diff are usually the same tagger's output at different times,
    // and the separators are the first thing to drift.
    [Arguments(@"D:\M\Benassi Bros.; Dhany - Every Single Day.flac", "benassi bros day dhany every")]
    // "Single" here is the release-format word, not part of the title. It is
    // dropped for the same reason as "Remaster": one side carries it, the
    // other does not, and it never distinguishes two different tracks.
    [Arguments(@"D:\M\a-ha - Manhattan Skyline - 2016 Remaster.flac", "ha manhattan skyline")]
    [Arguments(@"D:\M\Eric Prydz - Proper Education - Radio Edit.flac", "education eric proper prydz")]
    // & and "and" are the same word to a tagger, and case never means anything.
    [Arguments(@"D:\M\Simon & Garfunkel - THE BOXER.flac", "boxer garfunkel simon")]
    // A word repeated in artist and title collapses: the set is what matters,
    // so "Duran Duran" must not out-weigh the rest of the name.
    [Arguments(@"D:\M\Duran Duran - Rio.flac", "duran rio")]
    public async Task Words_reduces_a_file_name_to_its_track_words(string path, string expected)
    {
        await Assert.That(Words(path)).IsEqualTo(expected);
    }

    [Test]
    public async Task The_same_words_name_the_same_track()
    {
        await Assert.That(TitleTwin.SameTrack(
            TitleTwin.Words(@"A\Guano Apes - Open Your Eyes.flac"),
            TitleTwin.Words(@"B\Guano Apes - Open Your Eyes.mp3"))).IsTrue();
    }

    [Test]
    [Arguments(@"50 Cent; Olivia - Candy Shop.flac", @"50 Cent - Candy Shop.flac")]
    [Arguments(@"Thomas Bergersen; Two Steps from Hell - Heart of Courage.flac",
               @"Thomas Bergersen - Heart of Courage.flac")]
    [Arguments(@"Eminem; Hailie Jade - My Dad's Gone Crazy.flac", @"Eminem - My Dad's Gone Crazy.flac")]
    public async Task Extra_credited_artists_still_name_the_same_track(string a, string b)
    {
        // The commonest way two libraries disagree about a name: one side
        // credits every featured artist, the other only the headline act. The
        // shorter name is then a strict subset of the longer, which is the
        // shape this accepts, and it is NOT symmetric similarity: counting the
        // extra artists against the match rejects most of these outright.
        await Assert.That(TitleTwin.SameTrack(TitleTwin.Words(a), TitleTwin.Words(b))).IsTrue();
        await Assert.That(TitleTwin.SameTrack(TitleTwin.Words(b), TitleTwin.Words(a))).IsTrue();
    }

    [Test]
    [Arguments(@"Alexei Scutari - Dacia.flac", @"Alexei Scutari - Spirit.flac")]
    [Arguments(@"Daft Punk - One More Time.flac", @"Daft Punk - Aerodynamic.flac")]
    public async Task Two_tracks_by_one_artist_are_not_the_same_track(string a, string b)
    {
        // The failure mode that matters. An artist's whole catalogue shares the
        // artist words, so a rule that accepted a partial overlap would twin
        // every track a prolific artist has on either side.
        await Assert.That(TitleTwin.SameTrack(TitleTwin.Words(a), TitleTwin.Words(b))).IsFalse();
    }

    [Test]
    public async Task A_short_name_inside_a_longer_one_is_not_enough()
    {
        // Containment alone would twin "Intro" with any track whose name
        // happens to contain it, so a subset must still share real evidence.
        await Assert.That(TitleTwin.SameTrack(
            TitleTwin.Words(@"A\Portishead - Roads.flac"),
            TitleTwin.Words(@"B\Portishead - Roads To Nowhere At All.flac"))).IsFalse();
    }

    [Test]
    public async Task A_name_with_nothing_left_after_the_noise_twins_nothing()
    {
        // "The Remix" reduces to no words at all. Two such files are not
        // evidence of anything, and must not twin each other.
        await Assert.That(TitleTwin.SameTrack(
            TitleTwin.Words(@"A\The Remix.flac"),
            TitleTwin.Words(@"B\The Remix.flac"))).IsFalse();
    }

    [Test]
    public async Task FindAcrossRoots_looks_only_under_the_other_roots()
    {
        // Two copies in ONE folder are a duplicate, not a difference between
        // folders. A diff asks whether the other side has the track, so a twin
        // under the same root answers the wrong question.
        (string Path, string Root)[] all =
        [
            (@"A\Guano Apes - Open Your Eyes.flac", "A"),
            (@"A\Guano Apes - Open Your Eyes (1).flac", "A"),
        ];
        var found = TitleTwin.FindAcrossRoots([all[0]], all);
        await Assert.That(found.Count).IsEqualTo(0);
    }

    [Test]
    public async Task FindAcrossRoots_finds_a_twin_that_matched_audio_of_its_own()
    {
        // The haystack is every scanned file, not only the other unpaired
        // ones. A track can be grouped on one side (two copies there that
        // matched each other) while the copy on this side matched nothing, and
        // "is it over there at all" is still yes.
        (string Path, string Root)[] all =
        [
            (@"A\Guano Apes - Open Your Eyes.flac", "A"),
            (@"B\Guano Apes - Open Your Eyes.flac", "B"),
            (@"B\Guano Apes - Open Your Eyes - Radio Edit.flac", "B"),
        ];
        var found = TitleTwin.FindAcrossRoots([all[0]], all);
        await Assert.That(found.ContainsKey(@"A\Guano Apes - Open Your Eyes.flac")).IsTrue();
        await Assert.That(found[@"A\Guano Apes - Open Your Eyes.flac"]).IsEqualTo(@"B\Guano Apes - Open Your Eyes.flac");
    }

    [Test]
    public async Task FindAcrossRoots_reports_nothing_for_a_track_the_other_side_lacks()
    {
        (string Path, string Root)[] all =
        [
            (@"A\Portishead - Roads.flac", "A"),
            (@"B\Daft Punk - Aerodynamic.flac", "B"),
        ];
        await Assert.That(TitleTwin.FindAcrossRoots([all[0]], all).Count).IsEqualTo(0);
    }

    [Test]
    public async Task FindAcrossRoots_prefers_the_same_words_over_a_shorter_superset()
    {
        // The superset's file name is the shorter of the two on purpose: if
        // length were asked first, it would win, and it must not.
        (string Path, string Root)[] all =
        [
            (@"A\Thomas Bergersen - Heart of Courage.flac", "A"),
            (@"B\Thomas Bergersen -- Heart of Courage !!!.flac", "B"),
            (@"B\Thomas Bergersen - Heart of Courage Z.flac", "B"),
        ];
        var found = TitleTwin.FindAcrossRoots([all[0]], all);
        await Assert.That(found[all[0].Path]).IsEqualTo(@"B\Thomas Bergersen -- Heart of Courage !!!.flac");
    }

    [Test]
    public async Task FindAcrossRoots_lets_the_same_words_displace_an_earlier_superset()
    {
        // The mirror of the test above: the superset is met first here, so
        // this pins that an exact twin replaces it, where that test pins
        // that it is not replaced.
        (string Path, string Root)[] all =
        [
            (@"A\Thomas Bergersen - Heart of Courage.flac", "A"),
            (@"B\Thomas Bergersen - Heart of Courage Z.flac", "B"),
            (@"B\Thomas Bergersen -- Heart of Courage !!!.flac", "B"),
        ];
        var found = TitleTwin.FindAcrossRoots([all[0]], all);
        await Assert.That(found[all[0].Path]).IsEqualTo(@"B\Thomas Bergersen -- Heart of Courage !!!.flac");
    }

    [Test]
    public async Task FindAcrossRoots_settles_a_dead_heat_by_path()
    {
        // Same words, same name length, different roots. The winner sits in
        // the middle of the candidates, so taking the first one seen fails,
        // and so does taking the last.
        (string Path, string Root)[] all =
        [
            (@"A\Guano Apes - Open Your Eyes.flac", "A"),
            (@"C\Guano Apes - Open Your Eyes.flac", "C"),
            (@"B\Guano Apes - Open Your Eyes.flac", "B"),
            (@"D\Guano Apes - Open Your Eyes.flac", "D"),
        ];
        var found = TitleTwin.FindAcrossRoots([all[0]], all);
        await Assert.That(found[all[0].Path]).IsEqualTo(@"B\Guano Apes - Open Your Eyes.flac");
    }

    [Test]
    [Arguments(@"D:\M\Artist Name - Song Title - Radio Edit.flac", "artist edit name radio song title")]
    [Arguments(@"D:\M\Artist Name feat. Guest - Song Title - 2016 Remaster.flac", "artist guest name remaster song title")]
    public async Task VersionedWords_keep_the_version_words_and_drop_the_rest_of_the_noise(string path, string expected) =>
        await Assert.That(string.Join(" ", TitleTwin.VersionedWords(path).Order(StringComparer.Ordinal))).IsEqualTo(expected);

    [Test]
    [Arguments(@"A\Artist Name - Song Title - Radio Edit.flac", @"B\Artist Name - Song Title (Radio Edit).mp3", 0)]
    // A tagger that cannot put a question mark in a file name swaps it for a
    // fullwidth one or drops it. Neither is a word, so both read the same.
    [Arguments(@"A\Artist Name - How Much Is It？.flac", @"B\Artist Name - How Much Is It.flac", 0)]
    [Arguments(@"A\Song Title Here.flac", @"B\song title here.opus", 0)]
    [Arguments(@"A\Artist Name; Guest Two - Song Title.flac", @"B\Artist Name - Song Title.flac", 1)]
    [Arguments(@"A\Artist Name; Guest Two - Song Title - Live - 2001.flac", @"B\Artist Name - Song Title - Live - 2001.flac", 1)]
    [Arguments(@"A\Artist Name - Song Title - Radio Edit.flac", @"B\Artist Name - Song Title - Extended Mix.flac", 2)]
    [Arguments(@"A\Artist Name - Song Title.flac", @"B\Artist Name - Song Title Reprise.flac", 2)]
    // An en dash is not a " - ": nothing splits, so there are no artists, and a
    // name this reader cannot take apart falls back to the loosest rank.
    [Arguments(@"A\Artist Name – Song Title.flac", @"B\Artist Name; Guest Two – Song Title.flac", 2)]
    public async Task NameRank_says_how_closely_two_twin_names_agree(string a, string b, int expected)
    {
        // Every case must be a 0.25.0 twin first: the rank only orders twins.
        await Assert.That(TitleTwin.SameTrack(TitleTwin.Words(a), TitleTwin.Words(b))).IsTrue();
        await Assert.That(TitleTwin.NameRank(a, b)).IsEqualTo(expected);
        await Assert.That(TitleTwin.NameRank(b, a)).IsEqualTo(expected);
    }

    [Test]
    public async Task CrossRootTwins_lists_each_cross_root_twin_pair_once_and_no_same_root_pair()
    {
        // Files 0 and 1 are twins inside one folder, which is a duplicate and the
        // other view's business. File 2 shares four words with each of them, so a
        // walk that did not remember what it had checked would list it four times.
        (string Path, string Root)[] files =
        [
            (@"A\Artist Name - Song Title.flac", "A"),
            (@"A\Artist Name; Guest Two - Song Title.flac", "A"),
            (@"B\Artist Name - Song Title.mp3", "B"),
            (@"B\Something Else Entirely.mp3", "B"),
        ];
        var pairs = TitleTwin.CrossRootTwins(files);
        await Assert.That(string.Join(" ", pairs.Select(p => $"{p.A}-{p.B}"))).IsEqualTo("0-2 1-2");
    }

    [Test]
    public async Task CrossRootTwins_holds_every_twin_FindAcrossRoots_names()
    {
        // The column of name matches and the list of suspects read one relation,
        // and the column promises that any file it names a twin for is also in a
        // pair the suspects are drawn from.
        (string Path, string Root)[] files =
        [
            (@"A\Artist Name - Song Title.flac", "A"),
            (@"A\Artist Name; Guest Two - Song Title.flac", "A"),
            (@"B\Artist Name - Song Title.mp3", "B"),
            (@"B\Something Else Entirely.mp3", "B"),
            (@"C\Artist Name - Song Title.wav", "C"),
        ];
        var found = TitleTwin.FindAcrossRoots(files, files);
        var pairs = TitleTwin.CrossRootTwins(files);

        // All but the odd one out have a twin, so the loop below has work to do.
        await Assert.That(found.Count).IsEqualTo(4);
        var missing = new List<string>();
        foreach (var (needle, twin) in found)
        {
            var a = Array.FindIndex(files, f => f.Path == needle);
            var b = Array.FindIndex(files, f => f.Path == twin);
            if (!pairs.Contains((Math.Min(a, b), Math.Max(a, b)))) missing.Add($"{a}-{b}");
        }
        await Assert.That(string.Join(" ", missing)).IsEqualTo("");
    }

    [Test]
    public async Task CrossRootTwins_lists_all_the_twins_and_only_the_twins_in_index_order()
    {
        // File 0 twins 1, 2 and 3, and 1 twins 3. A list that kept one twin per
        // file (the closest: 0-2 and 1-3) would lose 0-1 and 0-3. Files 2 and 3
        // share two words and file 4 shares words with every file, yet none of
        // those pairs are twins, so a list that took any shared word for a twin
        // would gain 0-4, 1-4, 2-3 and 2-4. From file 0, file 2 turns up first
        // (it holds file 0's first word), so a list left in the order it was
        // found would read "0-2 0-1 0-3 1-3".
        (string Path, string Root)[] files =
        [
            (@"A\Alpha Bravo Charlie Delta Echo.flac", "A"),
            (@"B\Bravo Charlie Delta.flac", "B"),
            (@"B\Alpha Bravo Charlie.flac", "B"),
            (@"C\Delta Charlie Bravo.flac", "C"),
            (@"C\Alpha Bravo Foxtrot.flac", "C"),
        ];
        var pairs = TitleTwin.CrossRootTwins(files);
        await Assert.That(string.Join(" ", pairs.Select(p => $"{p.A}-{p.B}"))).IsEqualTo("0-1 0-2 0-3 1-3");
    }
}
