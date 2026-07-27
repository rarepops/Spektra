using Spektra.Core;

namespace Spektra.Tests;

public class LaunchArgsTests
{
    // Stub predicates: anything ending in an audio-ish extension is a "file",
    // anything else that was named is a "folder". Keeps the parser's rules
    // under test without touching the disk.
    private static readonly Func<string, bool> IsFile =
        p => p.EndsWith(".flac", StringComparison.OrdinalIgnoreCase)
          || p.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
    private static readonly Func<string, bool> IsDir =
        p => p.StartsWith("D:\\Music", StringComparison.OrdinalIgnoreCase);

    private static LaunchRequest Parse(params string[] args) =>
        LaunchArgs.Parse(args, IsFile, IsDir);

    [Test]
    public async Task NoArguments_IsBare()
    {
        var r = Parse();
        await Assert.That(r.IsBare).IsTrue();
        await Assert.That(r.Files.Count).IsEqualTo(0);
        await Assert.That(r.Folders.Count).IsEqualTo(0);
        await Assert.That(r.Compare).IsNull();
        await Assert.That(r.DupesRoot).IsNull();
        await Assert.That(r.ManifestRoot).IsNull();
    }

    [Test]
    public async Task OneExistingFile_IsOneFile_AndNotBare()
    {
        var r = Parse("a.flac");
        await Assert.That(r.IsBare).IsFalse();
        await Assert.That(r.Files.Count).IsEqualTo(1);
        await Assert.That(r.Files[0]).IsEqualTo("a.flac");
    }

    [Test]
    public async Task SeveralFiles_KeepCommandLineOrder()
    {
        // Explorer hands a multi-selection in its own order; tabs must follow it.
        var r = Parse("c.flac", "a.mp3", "b.flac");
        await Assert.That(r.Files.Count).IsEqualTo(3);
        await Assert.That(r.Files[0]).IsEqualTo("c.flac");
        await Assert.That(r.Files[1]).IsEqualTo("a.mp3");
        await Assert.That(r.Files[2]).IsEqualTo("b.flac");
    }

    [Test]
    public async Task NonexistentPath_IsDropped_AndCanLeaveItBare()
    {
        var r = Parse("gone.wav");
        await Assert.That(r.Files.Count).IsEqualTo(0);
        await Assert.That(r.IsBare).IsTrue();
    }

    [Test]
    public async Task FolderAndMixedSelection_SplitByKind()
    {
        var r = Parse("D:\\Music\\Albums", "a.flac");
        await Assert.That(r.Folders.Count).IsEqualTo(1);
        await Assert.That(r.Folders[0]).IsEqualTo("D:\\Music\\Albums");
        await Assert.That(r.Files.Count).IsEqualTo(1);
        await Assert.That(r.Files[0]).IsEqualTo("a.flac");
    }

    [Test]
    public async Task Compare_BothPathsExist_YieldsPairAndNoFiles()
    {
        var r = Parse("--compare", "a.flac", "b.flac");
        await Assert.That(r.Compare).IsNotNull();
        await Assert.That(r.Compare!.PathA).IsEqualTo("a.flac");
        await Assert.That(r.Compare!.PathB).IsEqualTo("b.flac");
        await Assert.That(r.Compare!.AutoAlign).IsFalse();
        await Assert.That(r.Compare!.Mode).IsNull();
        await Assert.That(r.Files.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Compare_AutoAndMode_AreCaptured_ModeLowercased()
    {
        var r = Parse("--compare", "a.flac", "b.flac", "--auto", "--mode", "DIFF");
        await Assert.That(r.Compare!.AutoAlign).IsTrue();
        await Assert.That(r.Compare!.Mode).IsEqualTo("diff");
    }

    [Test]
    public async Task Compare_SecondPathMissing_FallsBackToPlainOpen()
    {
        // Today's behavior: a broken --compare must still open what it can.
        var r = Parse("--compare", "a.flac", "gone.wav");
        await Assert.That(r.Compare).IsNull();
        await Assert.That(r.Files.Count).IsEqualTo(1);
        await Assert.That(r.Files[0]).IsEqualTo("a.flac");
    }

    [Test]
    public async Task Compare_ModeWithoutAValue_LeavesModeNull()
    {
        var r = Parse("--compare", "a.flac", "b.flac", "--mode");
        await Assert.That(r.Compare).IsNotNull();
        await Assert.That(r.Compare!.Mode).IsNull();
    }

    [Test]
    public async Task DupesAndManifest_TakeTheFollowingFolder()
    {
        var dupes = Parse("--dupes", "D:\\Music\\Albums");
        await Assert.That(dupes.DupesRoot).IsEqualTo("D:\\Music\\Albums");
        await Assert.That(dupes.Folders.Count).IsEqualTo(0); // consumed by the switch

        var manifest = Parse("--manifest", "D:\\Music\\Albums");
        await Assert.That(manifest.ManifestRoot).IsEqualTo("D:\\Music\\Albums");
    }

    [Test]
    public async Task DupesWithBadOrMissingValue_IsDropped()
    {
        await Assert.That(Parse("--dupes", "D:\\Nope").DupesRoot).IsNull();
        await Assert.That(Parse("--dupes").DupesRoot).IsNull();
        await Assert.That(Parse("--dupes").IsBare).IsTrue();
    }

    [Test]
    public async Task DupesAndManifestTogether_WithAFileAlongside()
    {
        var r = Parse("--dupes", "D:\\Music\\A", "--manifest", "D:\\Music\\B", "x.flac");
        await Assert.That(r.DupesRoot).IsEqualTo("D:\\Music\\A");
        await Assert.That(r.ManifestRoot).IsEqualTo("D:\\Music\\B");
        await Assert.That(r.Files.Count).IsEqualTo(1);
    }

    [Test]
    public async Task UnknownFlag_IsIgnored_AndSurroundingPathsStillParse()
    {
        // A stale registry row from an older install must degrade to a normal
        // launch. The GUI never exits 2 the way the CLI does.
        var r = Parse("--wat", "a.flac");
        await Assert.That(r.Files.Count).IsEqualTo(1);
        await Assert.That(r.Files[0]).IsEqualTo("a.flac");
    }

    [Test]
    public async Task DashLeadingPathThatExists_IsTreatedAsAPath()
    {
        var r = LaunchArgs.Parse(["-weird-name.flac"], IsFile, IsDir);
        await Assert.That(r.Files.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Compare_WinsTheWholeLine_LeavingNoToolRoots()
    {
        // The compare path returns early, so a tool switch alongside it is
        // structurally impossible. MainWindow's dispatcher relies on that.
        var r = Parse("--compare", "a.flac", "b.flac", "--dupes", "D:\\Music\\Albums");
        await Assert.That(r.Compare).IsNotNull();
        await Assert.That(r.DupesRoot).IsNull();
        await Assert.That(r.ManifestRoot).IsNull();
    }

    [Test]
    public async Task SwitchFollowedByAnotherFlag_DoesNotEatIt()
    {
        var r = Parse("--manifest", "--dupes", "D:\\Music\\Albums");
        await Assert.That(r.ManifestRoot).IsNull();
        await Assert.That(r.DupesRoot).IsEqualTo("D:\\Music\\Albums");
    }

    [Test]
    public async Task PathStartingWithASingleDash_IsStillTakenAsAValue()
    {
        // Only a double dash reads as a flag: the CLI once regressed by
        // rejecting every "-" prefixed token, which broke negative numbers.
        var r = LaunchArgs.Parse(["--dupes", "-oddly-named"], IsFile, p => p == "-oddly-named");
        await Assert.That(r.DupesRoot).IsEqualTo("-oddly-named");
    }
}
