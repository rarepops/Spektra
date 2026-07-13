using Spektra.Core;

namespace Spektra.Tests;

public sealed class FolderTreeTests
{
    private const string Root = @"C:\music";
    private static string F(params string[] parts)
        => Path.Combine([Root, .. parts]);

    [Test]
    public async Task Build_groups_files_under_their_folders()
    {
        IReadOnlyList<string> files =
        [
            F("Album", "01.flac"),
            F("Album", "02.flac"),
            F("single.flac"),
        ];

        var forest = FolderTree.Build(Root, files);

        await Assert.That(forest.Count).IsEqualTo(2);
        await Assert.That(forest[0]).IsTypeOf<FolderTreeFolder>();
        await Assert.That(forest[0].Name).IsEqualTo("Album");
        await Assert.That(forest[1]).IsTypeOf<FolderTreeFile>();
        await Assert.That(forest[1].Name).IsEqualTo("single.flac");
    }

    [Test]
    public async Task Build_from_a_drive_root_does_not_eat_the_first_character()
    {
        IReadOnlyList<string> files =
        [
            @"C:\Album\01.flac",
            @"C:\single.flac",
        ];

        var forest = FolderTree.Build(@"C:\", files);

        await Assert.That(forest.Count).IsEqualTo(2);
        await Assert.That(forest[0]).IsTypeOf<FolderTreeFolder>();
        await Assert.That(forest[0].Name).IsEqualTo("Album");
        await Assert.That(forest[0].FullPath).IsEqualTo(@"C:\Album");
        await Assert.That(forest[1]).IsTypeOf<FolderTreeFile>();
        await Assert.That(forest[1].Name).IsEqualTo("single.flac");
    }

    [Test]
    public async Task Build_sorts_folders_before_files_then_by_name()
    {
        IReadOnlyList<string> files =
        [
            F("z.flac"),
            F("Beta", "1.flac"),
            F("Alpha", "1.flac"),
            F("a.flac"),
        ];

        var forest = FolderTree.Build(Root, files);

        await Assert.That(forest.Select(n => n.Name)
            .SequenceEqual(new[] { "Alpha", "Beta", "a.flac", "z.flac" })).IsTrue();
    }

    [Test]
    public async Task Build_nests_multiple_levels()
    {
        IReadOnlyList<string> files =
        [
            F("Album", "CD2", "02.flac"),
            F("Album", "01.flac"),
        ];

        var forest = FolderTree.Build(Root, files);
        var album = (FolderTreeFolder)forest[0];

        await Assert.That(album.Children[0]).IsTypeOf<FolderTreeFolder>();
        await Assert.That(album.Children[0].Name).IsEqualTo("CD2");
        await Assert.That(album.Children[1].Name).IsEqualTo("01.flac");
    }

    [Test]
    public async Task Files_walks_the_forest_depth_first()
    {
        IReadOnlyList<string> files =
        [
            F("Album", "CD2", "02.flac"),
            F("Album", "01.flac"),
            F("single.flac"),
        ];

        var forest = FolderTree.Build(Root, files);
        var paths = FolderTree.Files(forest).Select(f => f.FullPath).ToList();

        await Assert.That(paths.SequenceEqual(new[]
        {
            F("Album", "CD2", "02.flac"),
            F("Album", "01.flac"),
            F("single.flac"),
        })).IsTrue();
    }

    [Test]
    public async Task Files_on_a_single_leaf_yields_that_leaf()
    {
        var leaf = new FolderTreeFile("a.flac", F("a.flac"));
        await Assert.That(FolderTree.Files(leaf).Single().FullPath)
            .IsEqualTo(F("a.flac"));
    }

    [Test]
    [Arguments(new[] { true, true }, true)]
    [Arguments(new[] { false, false }, false)]
    public async Task AggregateCheck_uniform(bool[] states, bool expected)
    {
        await Assert.That(FolderTree.AggregateCheck(states))
            .IsEqualTo((bool?)expected);
    }

    [Test]
    public async Task AggregateCheck_mixed_is_indeterminate()
    {
        await Assert.That(FolderTree.AggregateCheck([true, false])).IsNull();
    }

    [Test]
    public async Task AggregateCheck_empty_is_false()
    {
        await Assert.That(FolderTree.AggregateCheck([])).IsFalse();
    }
}
