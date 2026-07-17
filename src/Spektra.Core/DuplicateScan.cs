using System.Collections.Concurrent;

namespace Spektra.Core;

public sealed record DupesProgress(string Phase, double Fraction, int Done, int Total);

public sealed record NotAnalyzedFile(string Path, string Reason);

public sealed record DupesGroupReport(
    DuplicateGroup Group, QualityRanking Quality,
    IReadOnlyDictionary<string, AuditRow> Rows, IReadOnlyDictionary<string, long> Sizes,
    long ReclaimableBytes);

public sealed record DupesResult(
    IReadOnlyList<DupesGroupReport> Groups, IReadOnlyList<NotAnalyzedFile> NotAnalyzed,
    int FilesScanned, long ReclaimableBytes);

/// Flat, stable export row: one per group member, winners first. Lives in Core
/// (not the CLI) so the desktop app's future Duplicates window can reuse the
/// same builder for its own grid/export instead of re-flattening DupesResult.
public sealed record DupesRow(
    int Group, string Label, string SamenessTier, string File, string? Codec,
    double? CutoffKhz, string Bandwidth, string Integrity, long SizeBytes,
    double Sameness, bool FoundByAudio, bool Winner, string QualityConfidence, string QualityReason);

/// The Dedup Destroyer engine: enumerate roots, get every file's audit row and
/// fingerprint (cached; one decode covers both when neither is cached), match
/// fingerprints, group, rank quality. View-only by design: nothing here ever
/// touches the files themselves.
public static class DuplicateScan
{
    /// Candidates must be within this much decoded duration of each other; a
    /// radio edit will not pair with the extended mix (spec: stated limit).
    public const double MaxDurationDeltaSeconds = 15.0;

    /// Files shorter than this fingerprint too little to trust; they are
    /// reported as not analyzed rather than silently dropped.
    public const double MinDurationSeconds = 20.0;

    /// Total-corpus-size cutoff, not a per-word cap: at or below this many
    /// usable files, candidate pairs are generated directly (see the comment
    /// at the call site) instead of through FingerprintIndex, because the
    /// naive O(n^2) all-pairs comparison is trivially cheap at this size (the
    /// pair count stays around 2000) so building the index buys nothing.
    /// This value is INDEPENDENT of and unrelated to
    /// FingerprintIndex.MaxPostingFanout (a per-word posting-list length cap
    /// that answers a different question); the two happen to share the
    /// number 64 by coincidence, not by design. Changing either one must not
    /// be assumed to affect the other's justification.
    private const int AllPairsBelow = 64;

    public static DupesResult Run(
        FfmpegPaths ffmpeg, IReadOnlyList<string> roots, int jobs,
        AuditCache? cache = null, bool fresh = false,
        IProgress<DupesProgress>? progress = null, CancellationToken ct = default,
        double minDurationSeconds = MinDurationSeconds)
    {
        var targets = new Dictionary<string, AuditTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
            foreach (var t in FolderAudit.CollectTargets(root, recursive: true))
                targets.TryAdd(t.Path, t);
        var files = targets.Values.OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToArray();

        var artifacts = new (AuditEntry? Entry, FingerprintEntry? Fp, string? Error)[files.Length];
        var done = 0;
        Parallel.ForEach(
            Partitioner.Create(Enumerable.Range(0, files.Length), EnumerablePartitionerOptions.NoBuffering),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs), CancellationToken = ct },
            i =>
            {
                artifacts[i] = AnalyzeOne(ffmpeg, files[i], cache, fresh, ct);
                var n = Interlocked.Increment(ref done);
                progress?.Report(new DupesProgress("Analyzing", (double)n / files.Length, n, files.Length));
            });

        var usable = new List<int>();
        var notAnalyzed = new List<NotAnalyzedFile>();
        for (var i = 0; i < files.Length; i++)
        {
            var (entry, fp, error) = artifacts[i];
            if (entry is null || fp is null)
                notAnalyzed.Add(new NotAnalyzedFile(files[i].Path, error ?? "analysis incomplete"));
            else if (fp.Fingerprint.DurationSeconds < minDurationSeconds)
                notAnalyzed.Add(new NotAnalyzedFile(files[i].Path, $"shorter than {minDurationSeconds:0} s"));
            else
                usable.Add(i);
        }
        ct.ThrowIfCancellationRequested();

        // The word index is a candidate-generation PRE-FILTER, not a precision
        // gate (FingerprintMatcher's own MinVotes/MidThreshold below is that);
        // it exists so a large library never pays an all-pairs match. Below
        // AllPairsBelow usable files the naive pair count is small enough
        // (<= ~2000 direct matches) that skipping the index costs nothing
        // measurable and avoids a real failure mode: short or low-entropy
        // recordings (brief jingles, sweeps, sub-minute clips) can have so
        // few distinct fingerprint words that two genuine duplicates share
        // fewer than DefaultMinSharedWords of them even though the direct
        // matcher clears MidThreshold comfortably, so the index would silently
        // drop a real match before the matcher ever sees the pair.
        IReadOnlyList<(int A, int B)> candidates;
        if (usable.Count <= AllPairsBelow)
        {
            candidates = AllPairs(usable.Count);
        }
        else
        {
            var index = new FingerprintIndex();
            foreach (var i in usable)
                index.Add(artifacts[i].Fp!.Fingerprint); // index id == position in `usable`
            candidates = index.CandidatePairs();
        }

        var pairs = new List<(int A, int B, FingerprintMatcher.MatchResult Match)>();
        var checked_ = 0;
        foreach (var (a, b) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var fa = artifacts[usable[a]].Fp!.Fingerprint;
            var fb = artifacts[usable[b]].Fp!.Fingerprint;
            if (Math.Abs(fa.DurationSeconds - fb.DurationSeconds) <= MaxDurationDeltaSeconds
                && FingerprintMatcher.Match(fa, fb) is { } m
                && m.Similarity >= FingerprintMatcher.MidThreshold)
                pairs.Add((a, b, m));
            checked_++;
            progress?.Report(new DupesProgress(
                "Matching", (double)checked_ / Math.Max(1, candidates.Count), checked_, candidates.Count));
        }

        var identities = usable
            .Select(i => new DuplicateGrouper.FileIdentity(
                files[i].Path, artifacts[i].Fp!.Artist, artifacts[i].Fp!.Title))
            .ToList();
        var groups = DuplicateGrouper.Group(identities, pairs);

        var simByPair = new Dictionary<(string, string), double>();
        foreach (var (a, b, m) in pairs)
            simByPair[PairKey(identities[a].Path, identities[b].Path)] = m.Similarity;

        var entryByPath = new Dictionary<string, AuditEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in usable)
            entryByPath[files[i].Path] = artifacts[i].Entry!;

        var reports = new List<DupesGroupReport>();
        foreach (var g in groups)
        {
            var rows = g.Members.ToDictionary(
                m => m.Path, m => entryByPath[m.Path].Row, StringComparer.OrdinalIgnoreCase);
            var sizes = g.Members.ToDictionary(
                m => m.Path, m => targets[m.Path].SizeBytes, StringComparer.OrdinalIgnoreCase);
            var ranking = QualityRanker.Rank(
                [.. g.Members.Select(m => (m.Path, rows[m.Path]))],
                (x, y) => simByPair.GetValueOrDefault(PairKey(x, y)));
            var winnerSize = ranking.Winners.Max(w => sizes[w]);
            var reclaimable = sizes.Values.Sum() - winnerSize;
            reports.Add(new DupesGroupReport(g, ranking, rows, sizes, reclaimable));
        }

        return new DupesResult(
            [.. reports.OrderByDescending(r => r.ReclaimableBytes)],
            notAnalyzed, files.Length, reports.Sum(r => r.ReclaimableBytes));
    }

    /// Flat export rows, one per member, winners first inside each group.
    public static IReadOnlyList<DupesRow> ToRows(DupesResult result) =>
        [.. result.Groups.SelectMany(g => g.Group.Members
            .OrderByDescending(m => g.Quality.Winners.Contains(m.Path))
            .ThenBy(m => m.Path, StringComparer.OrdinalIgnoreCase)
            .Select(m => new DupesRow(
                g.Group.Id, g.Group.Label, g.Group.SamenessTier, m.Path,
                g.Rows[m.Path].Codec, g.Rows[m.Path].CutoffHz / 1000.0,
                g.Rows[m.Path].Bandwidth, g.Rows[m.Path].Integrity, g.Sizes[m.Path],
                m.Sameness, m.FoundByAudio, g.Quality.Winners.Contains(m.Path),
                g.Quality.Confidence, g.Quality.Reason)))];

    private static (string, string) PairKey(string x, string y) =>
        string.CompareOrdinal(x, y) <= 0 ? (x, y) : (y, x);

    private static List<(int A, int B)> AllPairs(int n)
    {
        var pairs = new List<(int, int)>(n * (n - 1) / 2);
        for (var a = 0; a < n; a++)
            for (var b = a + 1; b < n; b++)
                pairs.Add((a, b));
        return pairs;
    }

    private static (AuditEntry?, FingerprintEntry?, string?) AnalyzeOne(
        FfmpegPaths ffmpeg, AuditTarget target, AuditCache? cache, bool fresh, CancellationToken ct)
    {
        var entry = fresh ? null : cache?.TryGet(target);
        var fp = fresh ? null : cache?.TryGetFingerprint(target);
        if (entry is not null && fp is not null) return (entry, fp, null);
        try
        {
            // Metadata is probed here for the extractor's sample rate and the
            // tag ride-along; AnalyzeFile probes again internally. Two ffprobe
            // runs cost tens of milliseconds against a decode that costs
            // seconds, and only on cache misses.
            var meta = new AnalysisSession(ffmpeg).ReadMetadata(target.Path);
            if (entry is null)
            {
                var extractor = new FingerprintExtractor(meta.SampleRate);
                var result = FolderAudit.AnalyzeFile(ffmpeg, target.Path, ct, chunk => extractor.Feed(chunk));
                if (result.Report.Error is not null)
                    return (null, null, result.Report.Error);
                entry = new AuditEntry(target, result.ToRow(), result.HasProblem, FromCache: false);
                if (result.IntegrityError is null)
                    cache?.Put(target, entry.Row, entry.HasProblem);
                fp = StoreFingerprint(cache, target, extractor.Finish(), meta);
            }
            else
            {
                var print = new DecodedFingerprintSource(ffmpeg).Extract(target.Path, meta, ct);
                fp = StoreFingerprint(cache, target, print, meta);
            }
            return (entry, fp, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is AudioDecodeException or IOException or InvalidOperationException)
        {
            return (null, null, ex.Message);
        }
    }

    private static FingerprintEntry StoreFingerprint(
        AuditCache? cache, AuditTarget target, Fingerprint fp, AudioMetadata meta)
    {
        cache?.PutFingerprint(fp: fp, target: target,
            durationSeconds: fp.DurationSeconds, artist: meta.Artist, title: meta.Title);
        return new FingerprintEntry(fp, fp.DurationSeconds, meta.Artist, meta.Title);
    }
}
