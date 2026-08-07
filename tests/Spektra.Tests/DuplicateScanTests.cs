using Spektra.Core;

namespace Spektra.Tests;

public sealed class DuplicateScanTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static string P(string file) => Path.Combine(Fixtures, file);

    [Test]
    public async Task Run_FindsRenamedCopiesAcrossRoots_AndRanksTheLosslessOriginalFirst()
    {
        var rootA = Directory.CreateTempSubdirectory("dupes-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("dupes-b").FullName;
        try
        {
            // The true pair is the in-band tonal fixture plus its 128k encode
            // under an unrelated name (renames cannot hide a copy). chirp and
            // noise are strangers on both sides and must stay ungrouped: the
            // chirp/64k pair that used to play the duplicate here rode the
            // sparse-word floor that chance-corrected similarity removed.
            File.Copy(P("tones-a.wav"), Path.Combine(rootA, "Tones Song.wav"));
            File.Copy(P("chirp.wav"), Path.Combine(rootA, "chirp.wav"));
            File.Copy(P("tones-a-128.mp3"), Path.Combine(rootB, "completely unrelated name.mp3"));
            File.Copy(P("noise.wav"), Path.Combine(rootB, "noise.wav"));

            var result = DuplicateScan.Run(Ff, [rootA, rootB], jobs: 2, minDurationSeconds: 0);

            await Assert.That(result.FilesScanned).IsEqualTo(4);
            await Assert.That(result.NotAnalyzed).IsEmpty();
            await Assert.That(result.Groups).HasSingleItem();

            var g = result.Groups[0];
            await Assert.That(g.Group.Members.Count).IsEqualTo(2);
            await Assert.That(g.Quality.Winners.Single()).IsEqualTo(Path.Combine(rootA, "Tones Song.wav"));
            await Assert.That(g.Group.Members.Single(m => m.Path.EndsWith("name.mp3")).FoundByAudio).IsTrue();
            await Assert.That(g.ReclaimableBytes).IsGreaterThan(0);
            await Assert.That(result.ReclaimableBytes).IsEqualTo(g.ReclaimableBytes);
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Test]
    public async Task Run_ReportsUsableFilesThatMatchedNothing()
    {
        var rootA = Directory.CreateTempSubdirectory("diff-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("diff-b").FullName;
        try
        {
            // One track both folders hold, and one only A holds. "Only in this
            // folder" is the commonest kind of difference a folder diff shows,
            // and it is precisely what a duplicate scan discards today.
            File.Copy(P("tones-a.wav"), Path.Combine(rootA, "shared.wav"));
            File.Copy(P("tones-a-128.mp3"), Path.Combine(rootB, "shared.mp3"));
            File.Copy(P("noise.wav"), Path.Combine(rootA, "only-in-a.wav"));

            var result = DuplicateScan.Run(Ff, [rootA, rootB], jobs: 2, minDurationSeconds: 0);

            await Assert.That(result.Groups).HasSingleItem();
            await Assert.That(result.Unpaired).HasSingleItem();

            var lone = result.Unpaired[0];
            await Assert.That(Path.GetFileName(lone.Path)).IsEqualTo("only-in-a.wav");
            await Assert.That(lone.Root).IsEqualTo(rootA);
            await Assert.That(lone.SizeBytes).IsGreaterThan(0);
            await Assert.That(lone.Row.Bandwidth).IsNotNull();
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Test]
    public async Task Run_AttributesUnpairedFilesToTheDeepestRootHoldingThem()
    {
        // "Compare my library against the new rips folder inside it" is a
        // natural way to reach the folder diff, and nested roots get there:
        // AddRoot turns away only an exact repeat, and the CLI takes any paths
        // at all. Attribution is by longest matching root, NOT by whose walk
        // reached the file first, and the outer root is listed first here so
        // the two orders disagree. The split view builds one column per root
        // from this field, so filing the inner folder's file under the outer
        // one puts it in the wrong column, in plain sight.
        var outer = Directory.CreateTempSubdirectory("diff-outer").FullName;
        var inner = Directory.CreateDirectory(Path.Combine(outer, "New Rips")).FullName;
        try
        {
            File.Copy(P("chirp.wav"), Path.Combine(outer, "outer-only.wav"));
            File.Copy(P("noise.wav"), Path.Combine(inner, "inner-only.wav"));

            var result = DuplicateScan.Run(Ff, [outer, inner], jobs: 2, minDurationSeconds: 0);

            // The inner file sits under both roots and must still be one file:
            // analyzed twice it would pair with itself and read as a duplicate.
            await Assert.That(result.FilesScanned).IsEqualTo(2);
            await Assert.That(result.NotAnalyzed).IsEmpty();
            await Assert.That(result.Groups).IsEmpty();
            await Assert.That(result.Unpaired.Count).IsEqualTo(2);

            await Assert.That(result.Unpaired.Single(u => u.Path.EndsWith("inner-only.wav")).Root)
                .IsEqualTo(inner);
            await Assert.That(result.Unpaired.Single(u => u.Path.EndsWith("outer-only.wav")).Root)
                .IsEqualTo(outer);
        }
        finally
        {
            Directory.Delete(outer, recursive: true);
        }
    }

    [Test]
    public async Task IsSameTrack_FollowsSamenessTier_NotQuality()
    {
        // The two ideas are crossed on purpose. The confident group has a
        // clear quality winner and is still the same track; the weak group is
        // a perfect quality tie and is still not. A predicate written against
        // QualityRanking passes neither case, which is the point: whether a
        // FLAC beats an MP3 is the ordinary view's question, not a diff's.
        var confident = Report(tier: "High", members: ["a.flac", "a.mp3"], winners: ["a.flac"]);
        var weak = Report(tier: "Medium", members: ["a.flac", "b.flac"], winners: ["a.flac", "b.flac"]);

        await Assert.That(confident.IsSameTrack).IsTrue();
        await Assert.That(weak.IsSameTrack).IsFalse();
    }

    private static DupesGroupReport Report(string tier, string[] members, string[] winners) =>
        new(new DuplicateGroup(1, "label", tier,
                [.. members.Select(m => new DuplicateMember(m, 0.99, tier, false))]),
            new QualityRanking(winners, "High", "reason", members),
            new Dictionary<string, AuditRow>(),
            new Dictionary<string, long>(),
            ReclaimableBytes: 0);

    [Test]
    public async Task Run_ShortFiles_LandInNotAnalyzed_NotInGroups()
    {
        var root = Directory.CreateTempSubdirectory("dupes-short").FullName;
        try
        {
            File.Copy(P("chirp.wav"), Path.Combine(root, "a.wav"));
            File.Copy(P("chirp.wav"), Path.Combine(root, "b.wav"));
            var result = DuplicateScan.Run(Ff, [root], jobs: 1, minDurationSeconds: 3600);
            await Assert.That(result.Groups).IsEmpty();
            await Assert.That(result.NotAnalyzed.Count).IsEqualTo(2);
            await Assert.That(result.NotAnalyzed[0].Reason).Contains("shorter");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
