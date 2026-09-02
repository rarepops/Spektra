using Spektra.Core;

namespace Spektra.Tests;

public sealed class FileSequenceTests
{
    // Seven entries, stepped from index 3. An odd length with a distinct
    // middle, so an off-by-one cannot hide behind a symmetric list.
    private static readonly string[] Album =
    [
        @"C:\Music\Field Notes\01 ascent.flac",
        @"C:\Music\Field Notes\02 meridian.flac",
        @"C:\Music\Field Notes\03 nightfall.flac",
        @"C:\Music\Field Notes\04 undertow.flac",
        @"C:\Music\Field Notes\05 driftwood.flac",
        @"C:\Music\Field Notes\06 lantern.flac",
        @"C:\Music\Field Notes\07 harbour.flac",
    ];

    [Test]
    public async Task Next_moves_one_forward()
    {
        await Assert.That(FileSequence.Step(Album, Album[3], 1)).IsEqualTo(Album[4]);
    }

    [Test]
    public async Task Previous_moves_one_back()
    {
        await Assert.That(FileSequence.Step(Album, Album[3], -1)).IsEqualTo(Album[2]);
    }

    [Test]
    public async Task The_last_file_has_no_next()
    {
        await Assert.That(FileSequence.Step(Album, Album[^1], 1)).IsNull();
    }

    [Test]
    public async Task The_first_file_has_no_previous()
    {
        await Assert.That(FileSequence.Step(Album, Album[0], -1)).IsNull();
    }

    [Test]
    public async Task The_ends_still_step_inward()
    {
        // The end stops the walk in one direction only; the other must work,
        // or a sequence would be enterable and never leavable.
        await Assert.That(FileSequence.Step(Album, Album[0], 1)).IsEqualTo(Album[1]);
        await Assert.That(FileSequence.Step(Album, Album[^1], -1)).IsEqualTo(Album[^2]);
    }

    [Test]
    public async Task A_file_outside_the_list_has_no_neighbour()
    {
        // The shell hits this when the open file is not listed anywhere: a
        // deleted file, or one dropped in from outside every folder tab.
        const string stranger = @"C:\Music\Other\solitude.flac";
        await Assert.That(FileSequence.Step(Album, stranger, 1)).IsNull();
        await Assert.That(FileSequence.Step(Album, stranger, -1)).IsNull();
    }

    [Test]
    public async Task A_lone_file_is_both_ends_at_once()
    {
        string[] one = [Album[0]];
        await Assert.That(FileSequence.Step(one, Album[0], 1)).IsNull();
        await Assert.That(FileSequence.Step(one, Album[0], -1)).IsNull();
    }

    [Test]
    public async Task An_empty_list_has_no_neighbour()
    {
        await Assert.That(FileSequence.Step([], Album[0], 1)).IsNull();
    }

    [Test]
    public async Task The_current_path_matches_regardless_of_case()
    {
        // Path identity here must agree with the dictionaries the folder tab
        // is keyed by (_fileByPath, _rowIndex) and with OpenFile's duplicate
        // check, all of which are OrdinalIgnoreCase. A stricter rule here
        // would make the walk disagree with the tab dedupe.
        await Assert.That(FileSequence.Step(Album, @"C:\MUSIC\FIELD NOTES\04 UNDERTOW.FLAC", 1))
            .IsEqualTo(Album[4]);
    }

    [Test]
    public async Task Duplicate_entries_step_from_the_first_match()
    {
        // Two folder tabs on overlapping roots can list one file twice; the
        // walk must be deterministic rather than depending on which copy the
        // scan happened to reach.
        string[] repeated = [Album[0], Album[1], Album[0], Album[2]];
        await Assert.That(FileSequence.Step(repeated, Album[0], 1)).IsEqualTo(Album[1]);
    }
}
