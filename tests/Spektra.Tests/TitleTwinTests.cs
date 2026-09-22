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
}
