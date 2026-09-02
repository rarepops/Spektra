using Spektra.Core;

namespace Spektra.App;

/// One channel's finished analysis: the overview document, its bandwidth
/// verdict, and any loudness measured while that channel was active.
public sealed record ChannelOverview(
    SpectrogramDocument Document, LosslessVerdict? Verdict, LoudnessReport? Loudness);

/// Finished analyses kept across tabs, so that coming back to a file (Ctrl+Left
/// after Ctrl+Right, a second Ctrl+D, a tab opened again) shows at once
/// instead of decoding again. One record per file holds every finished
/// overview keyed by the settings and channel that produced it, plus the
/// integrity report, which is a whole-file measurement. The budget is bytes
/// of column data (Preferences, applied live); the least recently used FILE
/// goes first, whole, so a half-remembered file never happens. A record
/// carries the file's size and modified time, the audit cache's identity,
/// and one whose file has changed on disk is dropped on lookup, not served.
///
/// A document's own channel cache stays the store for what is on screen:
/// this only decides whether the NEXT load is instant, so a budget of 0
/// mid-session changes nothing visible. UI thread only, like that cache.
public sealed class OverviewCache
{
    /// Everything remembered for one file at one set of settings, by channel
    /// index (0 = Mix, i = channel i-1, the DocumentViewModel convention).
    public sealed record Snapshot(
        IReadOnlyDictionary<int, ChannelOverview> Overviews, IntegrityReport? Integrity);

    private sealed class FileRecord(long size, long mtimeTicks)
    {
        public long Size { get; } = size;
        public long MtimeTicks { get; } = mtimeTicks;
        public Dictionary<(SpectrogramSettings Settings, int Channel), ChannelOverview> Overviews { get; } = [];
        public IntegrityReport? Integrity { get; set; }
        public long Bytes => Overviews.Values.Sum(o => o.Document.ByteSize);
        public bool Matches(long size, long mtimeTicks) => Size == size && MtimeTicks == mtimeTicks;
    }

    private readonly ByteBudgetCache<string, FileRecord> _files;

    public OverviewCache(long budgetBytes) =>
        _files = new ByteBudgetCache<string, FileRecord>(
            budgetBytes, r => r.Bytes, StringComparer.OrdinalIgnoreCase);

    /// Bytes of column data kept. Lowering it evicts at once.
    public long Budget { get => _files.Budget; set => _files.Budget = value; }

    public long Bytes => _files.Bytes;
    public int Count => _files.Count;

    /// What is remembered for this file at these settings, or null when
    /// nothing is: never seen, evicted, gone from disk, or changed since.
    public Snapshot? Lookup(string path, SpectrogramSettings settings)
    {
        var record = Current(path);
        if (record is null) return null;
        var overviews = new Dictionary<int, ChannelOverview>();
        foreach (var ((s, channel), overview) in record.Overviews)
            if (s == settings) overviews[channel] = overview;
        if (overviews.Count == 0 && record.Integrity is null) return null;
        return new Snapshot(overviews, record.Integrity);
    }

    public void Put(string path, SpectrogramSettings settings, int channelIndex, ChannelOverview overview)
    {
        if (Writable(path) is not { } record) return;
        record.Overviews[(settings, channelIndex)] = overview;
        _files.Set(path, record); // re-measures, refreshes recency, evicts
    }

    public void PutIntegrity(string path, IntegrityReport report)
    {
        if (Writable(path) is not { } record) return;
        record.Integrity = report;
        _files.Set(path, record);
    }

    /// F5: an explicit reload is a reload.
    public void Forget(string path) => _files.Remove(path);

    /// The record for a file as it is on disk now, or null: never seen, or
    /// the file changed or vanished, in which case the stale record goes.
    private FileRecord? Current(string path)
    {
        if (!_files.TryGet(path, out var record)) return null;
        if (Stamp(path) is { } stamp && record.Matches(stamp.Size, stamp.MtimeTicks)) return record;
        _files.Remove(path);
        return null;
    }

    /// The record to write into: the current one, or a fresh one stamped
    /// from disk; null when the file cannot be stamped (gone, unreadable).
    private FileRecord? Writable(string path)
    {
        if (Current(path) is { } record) return record;
        return Stamp(path) is { } stamp ? new FileRecord(stamp.Size, stamp.MtimeTicks) : null;
    }

    private static (long Size, long MtimeTicks)? Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.Length, info.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
