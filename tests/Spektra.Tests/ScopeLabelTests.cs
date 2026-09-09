using Spektra.Core;

namespace Spektra.Tests;

public sealed class ScopeLabelTests
{
    [Test]
    [Arguments(@"D:\Music", null, "Music")]
    [Arguments(@"D:\Music\", null, "Music")]
    [Arguments(@"D:\Music", @"D:\Music\##GAMES##", "##GAMES##")]
    [Arguments(@"D:\Music", @"D:\Music\Albums\", "Albums")]
    [Arguments(@"D:\Music", @"D:\Music", "Music")]
    [Arguments(@"C:\", null, @"C:\")]
    [Arguments(@"D:\My_Music", null, "My__Music")]
    [Arguments(@"D:\Music", @"D:\Music\My_Mixes", "My__Mixes")]
    public async Task ForMenu_names_the_effective_scope(
        string root, string? scope, string expected)
    {
        await Assert.That(ScopeLabel.ForMenu(root, scope)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(@"D:\Music", null, "Music")]
    [Arguments(@"D:\Music", @"D:\Music\Albums\", "Albums")]
    [Arguments(@"C:\", null, @"C:\")]
    [Arguments(@"D:\My_Music", null, "My_Music")]
    public async Task For_names_the_same_scope_without_menu_escaping(
        string root, string? scope, string expected)
    {
        // The status bar is not a menu header: a doubled underscore would
        // render literally there, so the two callers need the same rule with
        // only the mnemonic escaping differing.
        await Assert.That(ScopeLabel.For(root, scope)).IsEqualTo(expected);
    }

    // Distinguish: labels for several folders shown side by side. A leaf name
    // alone is ambiguous exactly when it matters most, because the folders
    // worth diffing are usually two copies of one library and their leaves
    // match by construction.

    [Test]
    public async Task Distinguish_keeps_leaf_names_when_they_already_differ()
    {
        var labels = ScopeLabel.Distinguish([@"D:\FLAC\Rock", @"D:\MP3\Jazz"]);
        await Assert.That(string.Join(" | ", labels)).IsEqualTo("Rock | Jazz");
    }

    [Test]
    public async Task Distinguish_grows_colliding_labels_by_one_parent()
    {
        var labels = ScopeLabel.Distinguish([@"D:\FLAC\Album", @"D:\MP3\Album"]);
        await Assert.That(string.Join(" | ", labels)).IsEqualTo(@"FLAC\Album | MP3\Album");
    }

    [Test]
    public async Task Distinguish_keeps_growing_until_the_paths_diverge()
    {
        var labels = ScopeLabel.Distinguish([@"D:\A\Same\Album", @"D:\B\Same\Album"]);
        await Assert.That(string.Join(" | ", labels)).IsEqualTo(@"A\Same\Album | B\Same\Album");
    }

    [Test]
    public async Task Distinguish_only_grows_the_labels_that_collide()
    {
        // A third folder that was never ambiguous must not be lengthened just
        // because two others were: the shortest label that is still unique is
        // the one worth reading.
        var labels = ScopeLabel.Distinguish([@"D:\FLAC\Album", @"D:\MP3\Album", @"D:\Other\Jazz"]);
        await Assert.That(string.Join(" | ", labels)).IsEqualTo(@"FLAC\Album | MP3\Album | Jazz");
    }

    [Test]
    public async Task Distinguish_falls_back_to_the_whole_path_including_the_drive()
    {
        // Same tail on two drives: the drive is the only thing that differs,
        // and it is not a path segment, so the label has to become the path.
        var labels = ScopeLabel.Distinguish([@"D:\Music\Album", @"E:\Music\Album"]);
        await Assert.That(string.Join(" | ", labels)).IsEqualTo(@"D:\Music\Album | E:\Music\Album");
    }

    [Test]
    public async Task Distinguish_handles_drive_roots_and_trailing_separators()
    {
        var labels = ScopeLabel.Distinguish([@"D:\", @"D:\FLAC\Album\", @"D:\MP3\Album"]);
        await Assert.That(string.Join(" | ", labels)).IsEqualTo(@"D:\ | FLAC\Album | MP3\Album");
    }

    [Test]
    public async Task Distinguish_treats_a_case_difference_as_a_collision()
    {
        // Roots are deduplicated case-insensitively, so two labels differing
        // only in case would look identical to the eye scanning the columns.
        var labels = ScopeLabel.Distinguish([@"D:\FLAC\album", @"D:\MP3\Album"]);
        await Assert.That(string.Join(" | ", labels)).IsEqualTo(@"FLAC\album | MP3\Album");
    }

    [Test]
    public async Task Distinguish_returns_one_label_per_folder_in_order()
    {
        await Assert.That(string.Join(" | ", ScopeLabel.Distinguish([@"D:\Music\Album"]))).IsEqualTo("Album");
        await Assert.That(ScopeLabel.Distinguish([]).Count).IsEqualTo(0);
    }
}
