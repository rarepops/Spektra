namespace Spektra.Core;

/// One file's combined bandwidth + integrity audit.
public sealed record AuditResult(FileReport Report, IntegrityReport? Integrity, string? IntegrityError)
{
    public AuditRow ToRow() => Reporting.ToAuditRow(Report, Integrity, IntegrityError);

    /// True when the file warrants attention: unreadable, judged lossy or
    /// upsampled, or corrupt.
    public bool HasProblem =>
        Report.Error is not null
        || Report.Verdict?.Kind is VerdictKind.Lossy or VerdictKind.Upsampled
        || IntegrityError is not null
        || Integrity?.Status == IntegrityStatus.Corrupt;
}

/// Runs the combined bandwidth + integrity audit over many files in parallel
/// (bounded by `jobs`), returning results in input order. `progress` receives
/// the completed-file count. Cancellation throws OperationCanceledException.
/// Shared by the CLI `audit` verb and the GUI report export.
public static class FolderAudit
{
    public static AuditResult AnalyzeFile(FfmpegPaths ffmpeg, string path, CancellationToken ct = default)
    {
        var report = BandwidthReport.Analyze(ffmpeg, path, ct: ct);
        IntegrityReport? integrity = null;
        string? integrityError = null;
        if (report.Metadata is { } meta)
        {
            try { integrity = new IntegrityScanner(ffmpeg).Check(path, meta, ct); }
            catch (Exception ex) when (ex is AudioDecodeException or IOException) { integrityError = ex.Message; }
        }
        return new AuditResult(report, integrity, integrityError);
    }

    public static AuditResult[] Run(
        FfmpegPaths ffmpeg, IReadOnlyList<string> files, int jobs,
        IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var results = new AuditResult[files.Count];
        var done = 0;
        Parallel.For(0, files.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs), CancellationToken = ct },
            i =>
            {
                results[i] = AnalyzeFile(ffmpeg, files[i], ct);
                progress?.Report(Interlocked.Increment(ref done));
            });
        return results;
    }
}
