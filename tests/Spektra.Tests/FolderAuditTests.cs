using Spektra.Core;

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

    [Test]
    public async Task Run_KeepsInputOrder_DespiteParallelism()
    {
        AuditTarget[] targets = [T("sine-1khz.mp3"), T("chirp.wav"), T("noise.wav"), T("sine-1khz.flac")];
        var entries = FolderAudit.Run(Ff, targets, jobs: 4);
        await Assert.That(entries.Length).IsEqualTo(targets.Length);
        for (var i = 0; i < targets.Length; i++)
        {
            await Assert.That(entries[i].Target.Path).IsEqualTo(targets[i].Path);
            await Assert.That(entries[i].FromCache).IsFalse();
        }
    }

    [Test]
    public async Task Run_UnreadableFile_YieldsErrorRow_AndProblem_AndIsNotCached()
    {
        using var cache = AuditCache.Open(NewTempDb());
        var target = T("notaudio.txt");
        var entries = FolderAudit.Run(Ff, [target], jobs: 1, cache);
        await Assert.That(entries[0].Row.Error).IsNotNull();
        await Assert.That(entries[0].HasProblem).IsTrue();
        await Assert.That(cache.TryGet(target)).IsNull(); // error rows are never cached
    }

    [Test]
    public async Task HasProblem_TranscodeIsProblem_HonestLossyIsNot()
    {
        // A lossy wall inside a lossless container is a transcode.
        var transcode = FolderAudit.AnalyzeFile(Ff, P("transcode.flac"));
        await Assert.That(transcode.HasProblem).IsTrue();

        // The same wall in the mp3 it came from is just an mp3 being an mp3.
        var honest = FolderAudit.AnalyzeFile(Ff, P("chirp-mp3-64.mp3"));
        await Assert.That(honest.Report.Verdict?.Kind).IsEqualTo(VerdictKind.Lossy);
        await Assert.That(honest.HasProblem).IsFalse();

        var clean = FolderAudit.AnalyzeFile(Ff, P("chirp.wav"));
        await Assert.That(clean.HasProblem).IsFalse();
        await Assert.That(clean.ToRow().Error).IsNull();
    }

    [Test]
    public async Task Run_ReportsEachEntryOnce()
    {
        AuditTarget[] targets = [T("chirp.wav"), T("noise.wav"), T("sine-1khz.wav")];
        var progress = new Collector();
        FolderAudit.Run(Ff, targets, jobs: 2, progress: progress);
        await Assert.That(progress.Seen.Count).IsEqualTo(3);
        await Assert.That(progress.Seen.Select(e => e.Target.Path).OrderBy(p => p)
            .SequenceEqual(targets.Select(t => t.Path).OrderBy(p => p))).IsTrue();
    }

    [Test]
    public async Task Run_PreCancelled_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.That(() => FolderAudit.Run(Ff, [T("chirp.wav")], jobs: 1, ct: cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Run_ReplaysCacheHits_WithoutAnalyzing()
    {
        using var cache = AuditCache.Open(NewTempDb());
        AuditTarget[] targets = [T("chirp.wav"), T("noise.wav")];
        var first = FolderAudit.Run(Ff, targets, jobs: 2, cache);

        // Bogus ffmpeg paths: any analysis attempt would fail loudly.
        var bogus = new FfmpegPaths(@"C:\nope\ffmpeg.exe", @"C:\nope\ffprobe.exe");
        var progress = new Collector();
        var second = FolderAudit.Run(bogus, targets, jobs: 2, cache, progress: progress);

        await Assert.That(second.All(e => e.FromCache)).IsTrue();
        await Assert.That(second.Select(e => e.Row).SequenceEqual(first.Select(e => e.Row))).IsTrue();
        await Assert.That(progress.Seen.Count).IsEqualTo(2);
        await Assert.That(second.Select(e => e.Target.Path).SequenceEqual(targets.Select(t => t.Path))).IsTrue();
    }

    [Test]
    public async Task Run_Fresh_IgnoresAPoisonedEntry_AndOverwritesIt()
    {
        using var cache = AuditCache.Open(NewTempDb());
        var target = T("chirp.wav");
        var poisoned = new AuditRow(
            "chirp.wav", "flac", 1, 1, 1, 1, "Lossy", 1000, "Corrupt", 9, 9, true, null);
        cache.Put(target, poisoned, hasProblem: true);

        var entries = FolderAudit.Run(Ff, [target], jobs: 1, cache, fresh: true);

        await Assert.That(entries[0].FromCache).IsFalse();
        await Assert.That(entries[0].Row.Bandwidth).IsNotEqualTo("Lossy");
        await Assert.That(cache.TryGet(target)!.Row).IsEqualTo(entries[0].Row); // overwritten
    }

    [Test]
    public async Task Run_Cancelled_KeepsCompletedEntriesInCache()
    {
        var db = NewTempDb();
        AuditTarget[] targets = [T("chirp.wav"), T("noise.wav")];
        using var cts = new CancellationTokenSource();
        var cancelAfterFirst = new CancelAfterFirst(cts);
        using (var cache = AuditCache.Open(db))
        {
            await Assert.That(() => FolderAudit.Run(Ff, targets, jobs: 1, cache, progress: cancelAfterFirst, ct: cts.Token))
                .Throws<OperationCanceledException>();
        }
        using var reopened = AuditCache.Open(db);
        await Assert.That(reopened.TryGet(cancelAfterFirst.First!.Target)).IsNotNull();
    }

    private sealed class CancelAfterFirst(CancellationTokenSource cts) : IProgress<AuditEntry>
    {
        public AuditEntry? First;
        public void Report(AuditEntry value) { First ??= value; cts.Cancel(); }
    }

    [Test]
    public async Task CollectTargets_ReturnsOnlyAudio_WithRealSizesAndMtimes()
    {
        var dir = Directory.CreateTempSubdirectory("spektra-targets").FullName;
        try
        {
            File.Copy(P("chirp.wav"), Path.Combine(dir, "chirp.wav"));
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "not audio");

            var targets = FolderAudit.CollectTargets(dir);

            await Assert.That(targets).HasSingleItem();
            var t = targets.Single();
            await Assert.That(t.Path).IsEqualTo(Path.Combine(dir, "chirp.wav"));
            await Assert.That(t.SizeBytes).IsEqualTo(new FileInfo(t.Path).Length);
            await Assert.That(t.MtimeTicks).IsEqualTo(File.GetLastWriteTimeUtc(t.Path).Ticks);
            await Assert.That(t.SizeBytes > 0).IsTrue();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task AnalyzeFile_SilentGap_DetectsDropoutAndKeepsVerdict()
    {
        // Exercises the audit's combined decode: one pass has to feed both the
        // bandwidth verdict and the silence scan.
        var r = FolderAudit.AnalyzeFile(Ff, P("gap.wav"));
        await Assert.That(r.Report.Verdict).IsNotNull();
        await Assert.That(r.Integrity).IsNotNull();
        await Assert.That(r.Integrity!.Dropouts).HasSingleItem();
        await Assert.That(r.Integrity.Status).IsEqualTo(IntegrityStatus.Ok);
    }

    [Test]
    public async Task AnalyzeFile_CorruptFile_IsFlaggedAsProblem()
    {
        // corrupt.flac is truncated: either the decode errors out (error row) or
        // it decodes short and the integrity pass reports Corrupt.
        var r = FolderAudit.AnalyzeFile(Ff, P("corrupt.flac"));
        await Assert.That(r.HasProblem).IsTrue();
        await Assert.That(r.Report.Error is not null || r.Integrity?.Status == IntegrityStatus.Corrupt).IsTrue();
    }

    [Test]
    public async Task Run_AnalyzesOnlyTheGivenSubset_AndCachesOnlyThose()
    {
        using var cache = AuditCache.Open(NewTempDb());
        AuditTarget[] all =
        [
            T("chirp.wav"), T("noise.wav"), T("sine-1khz.wav"),
            T("sine-1khz.mp3"), T("sine-1khz.flac"),
        ];
        AuditTarget[] worklist = [all[0], all[1]];

        var entries = FolderAudit.Run(Ff, worklist, jobs: 2, cache);

        await Assert.That(entries.Length).IsEqualTo(2);
        await Assert.That(entries.Select(e => e.Target.Path)
            .SequenceEqual(worklist.Select(t => t.Path))).IsTrue();
        await Assert.That(cache.TryGet(all[0])).IsNotNull();
        await Assert.That(cache.TryGet(all[1])).IsNotNull();
        await Assert.That(cache.TryGet(all[2])).IsNull();
        await Assert.That(cache.TryGet(all[3])).IsNull();
        await Assert.That(cache.TryGet(all[4])).IsNull();
    }
}
