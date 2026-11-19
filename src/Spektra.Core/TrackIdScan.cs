using System.Collections.Concurrent;

namespace Spektra.Core;

public static class TrackIdScan
{
    /// The IDs of every file, keyed by its path ignoring case: from the cache where it holds them
    /// for this exact file, from the file's tags otherwise.
    public static IReadOnlyDictionary<string, TrackIds> Read(
        FfmpegPaths ffmpeg, IReadOnlyList<ScannedFile> files, int jobs, AuditCache? cache = null,
        IProgress<DupesProgress>? progress = null, CancellationToken ct = default)
    {
        var ids = new TrackIds[files.Count];
        var done = 0;
        Parallel.ForEach(
            Partitioner.Create(Enumerable.Range(0, files.Count), EnumerablePartitionerOptions.NoBuffering),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, jobs), CancellationToken = ct },
            i =>
            {
                ids[i] = ReadOne(ffmpeg, files[i], cache, ct);
                var n = Interlocked.Increment(ref done);
                // Throttled like the matching phase: a cache hit is far cheaper than a report, which
                // the UI posts to its dispatcher. The last file always reports, so the bar lands on full.
                if ((n & 0xFF) == 0 || n == files.Count)
                    progress?.Report(new DupesProgress("Reading tags", (double)n / files.Count, n, files.Count));
            });

        var byPath = new Dictionary<string, TrackIds>(files.Count, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.Count; i++)
            byPath[files[i].Path] = ids[i];
        return byPath;
    }

    private static TrackIds ReadOne(FfmpegPaths ffmpeg, ScannedFile file, AuditCache? cache, CancellationToken ct)
    {
        var target = new AuditTarget(file.Path, file.SizeBytes, file.MtimeTicks);
        if (cache?.TryGetTrackIds(target) is { } cached) return cached;
        try
        {
            var meta = new AnalysisSession(ffmpeg).ReadMetadata(file.Path, ct);
            // Cached when empty too: that a file carries no ID is worth remembering.
            var ids = new TrackIds(meta.SpotifyId, meta.Isrc);
            cache?.PutTrackIds(target, ids);
            return ids;
        }
        catch (Exception ex) when (ex is AudioDecodeException or IOException)
        {
            // Not cached: a file that could not be read this time must be tried again. Anything
            // else, a missing ffprobe among it, is a setup failure and propagates, as it does for the scan.
            return TrackIds.None;
        }
    }
}
