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
    private sealed class Collector : IProgress<AuditEntry>
    {
        public readonly List<AuditEntry> Seen = [];
        public void Report(AuditEntry value) { lock (Seen) Seen.Add(value); }
    }

    private static AuditTarget T(string file)
    {
        var info = new FileInfo(P(file));
        return new AuditTarget(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks);
    }

    private string NewTempDb() => Path.Combine(
        Directory.CreateTempSubdirectory("spektra-fa").FullName, "cache.db");

    [Fact]
    public void Run_KeepsInputOrder_DespiteParallelism()
    {
        AuditTarget[] targets = [T("sine-1khz.mp3"), T("chirp.wav"), T("noise.wav"), T("sine-1khz.flac")];
        var entries = FolderAudit.Run(Ff, targets, jobs: 4);
        Assert.Equal(targets.Length, entries.Length);
        for (var i = 0; i < targets.Length; i++)
        {
            Assert.Equal(targets[i].Path, entries[i].Target.Path);
            Assert.False(entries[i].FromCache);
        }
    }

    [Fact]
    public void Run_UnreadableFile_YieldsErrorRow_AndProblem_AndIsNotCached()
    {
        using var cache = AuditCache.Open(NewTempDb());
        var target = T("notaudio.txt");
        var entries = FolderAudit.Run(Ff, [target], jobs: 1, cache);
        Assert.NotNull(entries[0].Row.Error);
        Assert.True(entries[0].HasProblem);
        Assert.Null(cache.TryGet(target)); // error rows are never cached
    }

    [Fact]
    public void HasProblem_TranscodeIsProblem_HonestLossyIsNot()
    {
        // A lossy wall inside a lossless container is a transcode.
        var transcode = FolderAudit.AnalyzeFile(Ff, P("transcode.flac"));
        Assert.True(transcode.HasProblem);

        // The same wall in the mp3 it came from is just an mp3 being an mp3.
        var honest = FolderAudit.AnalyzeFile(Ff, P("chirp-mp3-64.mp3"));
        Assert.Equal(VerdictKind.Lossy, honest.Report.Verdict?.Kind);
        Assert.False(honest.HasProblem);

        var clean = FolderAudit.AnalyzeFile(Ff, P("chirp.wav"));
        Assert.False(clean.HasProblem);
        Assert.Null(clean.ToRow().Error);
    }

    [Fact]
    public void Run_ReportsEachEntryOnce()
    {
        AuditTarget[] targets = [T("chirp.wav"), T("noise.wav"), T("sine-1khz.wav")];
        var progress = new Collector();
        FolderAudit.Run(Ff, targets, jobs: 2, progress: progress);
        Assert.Equal(3, progress.Seen.Count);
        Assert.Equal(
            targets.Select(t => t.Path).OrderBy(p => p),
            progress.Seen.Select(e => e.Target.Path).OrderBy(p => p));
    }

    [Fact]
    public void Run_PreCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(
            () => FolderAudit.Run(Ff, [T("chirp.wav")], jobs: 1, ct: cts.Token));
    }

    [Fact]
    public void Run_ReplaysCacheHits_WithoutAnalyzing()
    {
        using var cache = AuditCache.Open(NewTempDb());
        AuditTarget[] targets = [T("chirp.wav"), T("noise.wav")];
        var first = FolderAudit.Run(Ff, targets, jobs: 2, cache);

        // Bogus ffmpeg paths: any analysis attempt would fail loudly.
        var bogus = new FfmpegPaths(@"C:\nope\ffmpeg.exe", @"C:\nope\ffprobe.exe");
        var progress = new Collector();
        var second = FolderAudit.Run(bogus, targets, jobs: 2, cache, progress: progress);

        Assert.All(second, e => Assert.True(e.FromCache));
        Assert.Equal(first.Select(e => e.Row), second.Select(e => e.Row));
        Assert.Equal(2, progress.Seen.Count);
        Assert.Equal(targets.Select(t => t.Path), second.Select(e => e.Target.Path));
    }

    [Fact]
    public void Run_Fresh_IgnoresAPoisonedEntry_AndOverwritesIt()
    {
        using var cache = AuditCache.Open(NewTempDb());
        var target = T("chirp.wav");
        var poisoned = new AuditRow(
            "chirp.wav", "flac", 1, 1, 1, 1, "Lossy", 1000, "Corrupt", 9, 9, true, null);
        cache.Put(target, poisoned, hasProblem: true);

        var entries = FolderAudit.Run(Ff, [target], jobs: 1, cache, fresh: true);

        Assert.False(entries[0].FromCache);
        Assert.NotEqual("Lossy", entries[0].Row.Bandwidth);
        Assert.Equal(entries[0].Row, cache.TryGet(target)!.Row); // overwritten
    }

    [Fact]
    public void Run_Cancelled_KeepsCompletedEntriesInCache()
    {
        var db = NewTempDb();
        AuditTarget[] targets = [T("chirp.wav"), T("noise.wav")];
        using var cts = new CancellationTokenSource();
        var cancelAfterFirst = new CancelAfterFirst(cts);
        using (var cache = AuditCache.Open(db))
        {
            Assert.ThrowsAny<OperationCanceledException>(() =>
                FolderAudit.Run(Ff, targets, jobs: 1, cache, progress: cancelAfterFirst, ct: cts.Token));
        }
        using var reopened = AuditCache.Open(db);
        Assert.NotNull(reopened.TryGet(cancelAfterFirst.First!.Target));
    }

    private sealed class CancelAfterFirst(CancellationTokenSource cts) : IProgress<AuditEntry>
    {
        public AuditEntry? First;
        public void Report(AuditEntry value) { First ??= value; cts.Cancel(); }
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
