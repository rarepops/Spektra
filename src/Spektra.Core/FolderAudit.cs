namespace Spektra.Core;

/// A file selected for audit, with the identity fields the cache keys on.
public sealed record AuditTarget(string Path, long SizeBytes, long MtimeTicks);

/// One audited file: the flat report row plus identity and provenance.
public sealed record AuditEntry(AuditTarget Target, AuditRow Row, bool HasProblem, bool FromCache);

/// One file's combined bandwidth + integrity audit.
public sealed record AuditResult(FileReport Report, IntegrityReport? Integrity, string? IntegrityError)
{
    public AuditRow ToRow() => Reporting.ToAuditRow(Report, Integrity, IntegrityError);

    /// True when the file warrants attention: unreadable, upsampled, corrupt,
    /// or a transcode (a Lossy verdict counts only when TranscodeCheck says
    /// the wall does not belong there - lossy content in a lossless container,
    /// or an mp3/aac far below its bitrate's expected cutoff).
    public bool HasProblem =>
        Report.Error is not null
        || Report.Verdict?.Kind is VerdictKind.Upsampled
        || (Report.Verdict?.Kind is VerdictKind.Lossy
            && TranscodeCheck.IsSuspectLossy(
                Report.Metadata?.Codec, Report.Metadata?.BitRateBps,
                Report.Metadata?.Channels, Report.Verdict.CutoffHz))
        || IntegrityError is not null
        || Integrity?.Status == IntegrityStatus.Corrupt;
}

/// How bad a single audited row is, low to high. Drives the grid's
/// severity filter and the tree marker colors.
public enum RowSeverity { Clean = 0, Suspect = 1, Problem = 2 }

/// How a folder worklist is scheduled for analysis. FolderOrder follows the
/// browse tree top to bottom; the size orders trade that for quicker early
/// results (smallest) or a faster-settling time estimate (largest).
public enum AnalysisOrder { FolderOrder = 0, SmallestFirst = 1, LargestFirst = 2 }

/// The combined bandwidth + integrity audit over a folder's files (see Run).
public static class FolderAudit
{
    public static RowSeverity Severity(AuditEntry entry) =>
        entry.HasProblem ? RowSeverity.Problem
        : entry.Row.Integrity is "Suspect" || entry.Row.Bandwidth is "Suspicious"
            ? RowSeverity.Suspect
            : RowSeverity.Clean;

    /// Enumerates a folder's audio files with the size/mtime identity the
    /// cache keys on (one stat per file).
    public static AuditTarget[] CollectTargets(string folder, bool recursive = true) =>
        BandwidthReport.FindAudioFiles(folder, recursive)
            .Select(p =>
            {
                var info = new FileInfo(p);
                return new AuditTarget(p, info.Length, info.LastWriteTimeUtc.Ticks);
            })
            .ToArray();

    /// Bandwidth + integrity for one file, sharing a single decode: the mono
    /// stream is teed through the spectrogram engine (bandwidth verdict) and the
    /// silence scan (dropouts + truncation) at once, so only the separate
    /// error-count pass decodes a second time (two decodes total, not three).
    public static AuditResult AnalyzeFile(FfmpegPaths ffmpeg, string path, CancellationToken ct = default)
    {
        var session = new AnalysisSession(ffmpeg);
        AudioMetadata meta;
        List<float[]> columns;
        IReadOnlyList<DropoutRegion> dropouts;
        long decodedSamples;
        try
        {
            meta = session.ReadMetadata(path);
            var settings = new SpectrogramSettings(WindowSize: 2048);
            (columns, dropouts, decodedSamples) = session.AnalyzeColumnsWithSilence(path, meta, settings, ct);
        }
        catch (Exception ex) when (ex is AudioDecodeException or IOException or InvalidOperationException)
        {
            // ffprobe or the decode failed: no verdict and no samples to judge, so
            // this is an error row and (as before) integrity is skipped.
            return new AuditResult(new FileReport(path, null, null, ex.Message), null, null);
        }

        var report = new FileReport(path, meta, CutoffAnalyzer.Analyze(columns, meta.SampleRate), null);

        IntegrityReport? integrity = null;
        string? integrityError = null;
        try { integrity = new IntegrityScanner(ffmpeg).CheckPreDecoded(path, meta, dropouts, decodedSamples, ct); }
        catch (Exception ex) when (ex is AudioDecodeException or IOException) { integrityError = ex.Message; }

        return new AuditResult(report, integrity, integrityError);
    }

    /// Order a worklist for analysis. FolderOrder keeps the given (tree)
    /// order; the size orders are stable, so equal-size files keep their
    /// tree order. Scheduling only: the result set is identical either way.
    public static IReadOnlyList<AuditTarget> OrderWorklist(
        IReadOnlyList<AuditTarget> worklist, AnalysisOrder order) => order switch
        {
            AnalysisOrder.SmallestFirst => [.. worklist.OrderBy(t => t.SizeBytes)],
            AnalysisOrder.LargestFirst => [.. worklist.OrderByDescending(t => t.SizeBytes)],
            _ => worklist,
        };

    /// Read-only positional cache lookup for a tree drop: one slot per target,
    /// the cached entry on a hit and null on a miss/stale/null-cache. Never
    /// runs ffmpeg, so the drop paints known verdicts instantly.
    public static AuditEntry?[] HydrateFromCache(
        AuditCache? cache, IReadOnlyList<AuditTarget> targets)
    {
        var entries = new AuditEntry?[targets.Count];
        if (cache is null)
            return entries;
        for (var i = 0; i < targets.Count; i++)
            entries[i] = cache.TryGet(targets[i]);
        return entries;
    }

    /// Runs the combined bandwidth + integrity audit over the targets, in
    /// parallel (bounded by `jobs`), returning entries in input order.
    /// Cache hits (unless `fresh`) replay first, in input order, without
    /// touching ffmpeg; misses are analyzed and upserted into the cache the
    /// moment each finishes, so cancel/crash keeps completed work. Error
    /// rows are never cached (a transient failure must not stick).
    /// `progress` receives every entry once. Cancellation throws
    /// OperationCanceledException. Shared by the CLI `audit` verb, the GUI
    /// report export, and the GUI folder tab.
    public static AuditEntry[] Run(
        FfmpegPaths ffmpeg, IReadOnlyList<AuditTarget> targets, int jobs,
        AuditCache? cache = null, bool fresh = false,
        IProgress<AuditEntry>? progress = null, CancellationToken ct = default)
    {
        var entries = new AuditEntry?[targets.Count];
        var missing = new List<int>();
        for (var i = 0; i < targets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var hit = fresh ? null : cache?.TryGet(targets[i]);
            if (hit is not null)
            {
                entries[i] = hit;
                progress?.Report(hit);
            }
            else missing.Add(i);
        }

        Parallel.For(0, missing.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs), CancellationToken = ct },
            k =>
            {
                var i = missing[k];
                var target = targets[i];
                var result = AnalyzeFile(ffmpeg, target.Path, ct);
                var entry = new AuditEntry(target, result.ToRow(), result.HasProblem, FromCache: false);
                if (result.Report.Error is null && result.IntegrityError is null)
                    cache?.Put(target, entry.Row, entry.HasProblem);
                entries[i] = entry;
                progress?.Report(entry);
            });

        return entries.Select(e => e!).ToArray();
    }
}
