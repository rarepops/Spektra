namespace Spektra.Core;

/// Imposter Buster's engine: the duplicate scan over the reference folder and the folders to check,
/// then an alignment of each pair that no audio group settles (the only audio work beyond the scan),
/// then ImposterSongs' rule over what both found.
public static class ImposterScan
{
    /// Scans the reference folder and the folders to check, then grades every checked copy
    /// against the reference files that carry its name. Report only: no file is touched.
    public static ImposterResult Run(
        FfmpegPaths ffmpeg, string reference, IReadOnlyList<string> roots, int jobs,
        AuditCache? cache = null, bool fresh = false,
        IProgress<DupesProgress>? progress = null, CancellationToken ct = default,
        double minDurationSeconds = DuplicateScan.MinDurationSeconds)
    {
        // The rule tells a reference file, and orders a song's copies, by the root the scan reports:
        // the full path DuplicateScan.Run makes of each root. The reference and the roots are spelled
        // the same way here, so they name the same strings.
        reference = Path.GetFullPath(reference);
        roots = [.. roots.Select(Path.GetFullPath)];
        if (roots.Count == 0)
            throw new ArgumentException("Name one or more folders to check.", nameof(roots));
        if (roots.FirstOrDefault(r => IsSameFolder(r, reference)) is { } same)
            throw new ArgumentException($"{same} is the reference folder itself.", nameof(roots));

        var scan = DuplicateScan.Run(ffmpeg, [reference, .. roots], jobs, cache, fresh, progress, ct, minDurationSeconds);
        var candidates = ImposterSongs.Candidates(scan.Files, reference);
        var pairs = ImposterSongs.PairsToAlign(candidates);
        var verdicts = SuspectLabeler.LabelAll(
            ffmpeg,
            [.. pairs.Select(p => new SuspectPair(
                p.Reference.Path, p.Reference.Root, p.Reference.Seconds, null,
                p.Copy.Path, p.Copy.Root, p.Copy.Seconds, null, NameStrength.SameName))],
            jobs, progress is null ? null : new CheckingProgress(progress), ct);

        // Build asks for exactly the pairs PairsToAlign listed, by the path strings the scan reported,
        // so the lookup cannot miss.
        var alignments = pairs.Zip(verdicts).ToDictionary(x => (x.First.Reference.Path, x.First.Copy.Path), x => x.Second);
        return ImposterSongs.Build(
            candidates, scan.Files, reference, roots,
            (referencePath, copyPath) => alignments[(referencePath, copyPath)],
            scan.Similarity, scan.FilesScanned);
    }

    /// Whether a folder is the reference folder itself, by full path ignoring case and a trailing separator.
    public static bool IsSameFolder(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    /// Passes each finished alignment on as the scan's last phase, at once and on the thread that
    /// reports it, so the reports keep the count order LabelAll gives them. Progress<T> would post
    /// each one and could deliver it out of order or after Run returns.
    private sealed class CheckingProgress(IProgress<DupesProgress> progress) : IProgress<LabelProgress>
    {
        public void Report(LabelProgress value) =>
            progress.Report(new DupesProgress("Checking", (double)value.Done / value.Total, value.Done, value.Total));
    }
}
