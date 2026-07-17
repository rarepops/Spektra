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
            File.Copy(P("chirp.wav"), Path.Combine(rootA, "Chirp Song.wav"));
            File.Copy(P("chirp-mp3-64.mp3"), Path.Combine(rootA, "Track01.mp3"));
            File.Copy(P("chirp-aac.m4a"), Path.Combine(rootB, "completely unrelated name.m4a"));
            File.Copy(P("noise.wav"), Path.Combine(rootB, "noise.wav"));

            var result = DuplicateScan.Run(Ff, [rootA, rootB], jobs: 2, minDurationSeconds: 0);

            await Assert.That(result.FilesScanned).IsEqualTo(4);
            await Assert.That(result.NotAnalyzed).IsEmpty();
            await Assert.That(result.Groups).HasSingleItem();

            var g = result.Groups[0];
            await Assert.That(g.Group.Members.Count).IsEqualTo(3);
            await Assert.That(g.Quality.Winners.Single()).IsEqualTo(Path.Combine(rootA, "Chirp Song.wav"));
            await Assert.That(g.Group.Members.Single(m => m.Path.EndsWith("name.m4a")).FoundByAudio).IsTrue();
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
