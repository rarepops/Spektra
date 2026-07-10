using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class FolderAuditTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static string P(string file) => Path.Combine(Fixtures, file);

    /// Synchronous, thread-safe progress collector (Progress<T> would marshal
    /// through a sync context and race the assertion).
    private sealed class Collector : IProgress<int>
    {
        public readonly List<int> Seen = [];
        public void Report(int value) { lock (Seen) Seen.Add(value); }
    }

    [Fact]
    public void Run_KeepsInputOrder_DespiteParallelism()
    {
        string[] files = [P("sine-1khz.mp3"), P("chirp.wav"), P("noise.wav"), P("sine-1khz.flac")];
        var results = FolderAudit.Run(Ff, files, jobs: 4);
        Assert.Equal(files.Length, results.Length);
        for (var i = 0; i < files.Length; i++)
            Assert.Equal(files[i], results[i].Report.Path);
    }

    [Fact]
    public void Run_UnreadableFile_YieldsErrorRow_AndProblem()
    {
        var results = FolderAudit.Run(Ff, [P("notaudio.txt")], jobs: 1);
        Assert.NotNull(results[0].Report.Error);
        Assert.True(results[0].HasProblem);
        Assert.NotNull(results[0].ToRow().Error);
    }

    [Fact]
    public void HasProblem_LossyIsProblem_CleanIsNot()
    {
        var lossy = FolderAudit.AnalyzeFile(Ff, P("chirp-mp3-64.mp3"));
        Assert.True(lossy.HasProblem);

        var clean = FolderAudit.AnalyzeFile(Ff, P("chirp.wav"));
        Assert.False(clean.HasProblem);
        Assert.Null(clean.ToRow().Error);
    }

    [Fact]
    public void Run_ReportsCompletedCounts_OnePerFile()
    {
        string[] files = [P("chirp.wav"), P("noise.wav"), P("sine-1khz.wav")];
        var progress = new Collector();
        FolderAudit.Run(Ff, files, jobs: 2, progress);
        var seen = progress.Seen.OrderBy(v => v).ToArray();
        Assert.Equal([1, 2, 3], seen);
    }

    [Fact]
    public void Run_PreCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(
            () => FolderAudit.Run(Ff, [P("chirp.wav")], jobs: 1, null, cts.Token));
    }

    [Fact]
    public void CollectTargets_ReturnsOnlyAudio_WithRealSizesAndMtimes()
    {
        var dir = Directory.CreateTempSubdirectory("spektra-targets").FullName;
        try
        {
            File.Copy(P("chirp.wav"), Path.Combine(dir, "chirp.wav"));
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "not audio");

            var targets = FolderAudit.CollectTargets(dir);

            var t = Assert.Single(targets);
            Assert.Equal(Path.Combine(dir, "chirp.wav"), t.Path);
            Assert.Equal(new FileInfo(t.Path).Length, t.SizeBytes);
            Assert.Equal(File.GetLastWriteTimeUtc(t.Path).Ticks, t.MtimeTicks);
            Assert.True(t.SizeBytes > 0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
