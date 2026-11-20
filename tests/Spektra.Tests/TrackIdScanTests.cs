using Spektra.Core;
using static Spektra.Tests.TestAudio;

namespace Spektra.Tests;

/// Reads the track IDs of four real files, made once for the class in a temp folder it deletes: a FLAC
/// whose tags hold an ISRC and a Spotify link, a FLAC with a Spotify ID tag, a WAV with no tags, and a
/// file that is not audio. The IDs are invented.
public sealed class TrackIdScanTests
{
    private const string LinkedId = "0aBcDeFgHiJkLmNoPqRsTu";
    private const string TaggedId = "1zYxWvUtSrQpOnMlKjIhGf";
    private const string Isrc = "USRC17607839";

    private static string _dir = null!;
    private static ScannedFile _linked = null!;
    private static ScannedFile _tagged = null!;
    private static ScannedFile _untagged = null!;
    private static ScannedFile _broken = null!;

    [Before(Class)]
    public static void MakeFiles()
    {
        _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "spektra-track-ids-" + Guid.NewGuid().ToString("N"))).FullName;

        var linked = Path.Combine(_dir, "linked.flac");
        Ffmpeg("-i", Fixture("tones-a.wav"), "-metadata", $"ISRC={Isrc}",
            "-metadata", $"comment=https://open.spotify.com/track/{LinkedId}", linked);
        var tagged = Path.Combine(_dir, "tagged.flac");
        Ffmpeg("-i", Fixture("tones-a.wav"), "-metadata", $"SPOTID={TaggedId}", tagged);
        var untagged = Path.Combine(_dir, "untagged.wav");
        File.Copy(Fixture("tones-a.wav"), untagged);
        // Not named .flac: under that name ffprobe accepts any bytes (exit 0, a flac stream with sample rate 0 and no tags), under .wav it refuses them and the probe throws.
        var broken = Path.Combine(_dir, "broken.wav");
        File.Copy(Fixture("notaudio.txt"), broken);

        _linked = Scanned(linked);
        _tagged = Scanned(tagged);
        _untagged = Scanned(untagged);
        _broken = Scanned(broken);
    }

    [After(Class)]
    public static void DeleteFiles() => Directory.Delete(_dir, recursive: true);

    // A file as the duplicate scan lists it: its path, size and modified time.
    private static ScannedFile Scanned(string path) => new(path, _dir, null, null)
    {
        SizeBytes = new FileInfo(path).Length,
        MtimeTicks = File.GetLastWriteTimeUtc(path).Ticks,
    };

    private static AuditTarget Target(ScannedFile file) => new(file.Path, file.SizeBytes, file.MtimeTicks);

    // A cache of its own for each test: the tests of a class run side by side.
    private static string NewDb() => Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db");

    // What a scan of this one file gives for it, or null when the scan lists none.
    private static TrackIds? IdsOf(ScannedFile file, AuditCache cache) =>
        TrackIdScan.Read(Ff, [file], jobs: 1, cache).TryGetValue(file.Path, out var ids) ? ids : null;

    [Test]
    public async Task Read_TakesTheIdsFromTheTags()
    {
        var ids = TrackIdScan.Read(Ff, [_linked, _tagged, _untagged, _broken], jobs: 2);

        await Assert.That(ids.Count).IsEqualTo(4);
        await Assert.That(ids[_linked.Path]).IsEqualTo(new TrackIds(LinkedId, Isrc));
        await Assert.That(ids[_tagged.Path]).IsEqualTo(new TrackIds(TaggedId, null));
        await Assert.That(ids[_untagged.Path]).IsEqualTo(TrackIds.None);
        await Assert.That(ids[_broken.Path]).IsEqualTo(TrackIds.None);

        // Keyed by path, ignoring case.
        await Assert.That(ids.ContainsKey(_tagged.Path.ToUpperInvariant())).IsTrue();
    }

    // A file that is not audio, and one that is gone, as a file deleted after the scan listed it is.
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Read_AFileThatCannotBeProbed_HasNoIds_AndIsNotCached(bool gone)
    {
        using var cache = AuditCache.Open(NewDb());
        var file = gone
            ? new ScannedFile(Path.Combine(_dir, "gone.wav"), _dir, null, null) { SizeBytes = 1, MtimeTicks = 2 }
            : _broken;

        var ids = TrackIdScan.Read(Ff, [file, _linked], jobs: 2, cache);

        await Assert.That(ids.ContainsKey(file.Path)).IsTrue();
        await Assert.That(ids[file.Path]).IsEqualTo(TrackIds.None);

        // Tried again next time, not remembered as having none. The readable file beside it is.
        await Assert.That(cache.TryGetTrackIds(Target(file))).IsNull();
        await Assert.That(cache.TryGetTrackIds(Target(_linked))).IsEqualTo(new TrackIds(LinkedId, Isrc));
    }

    [Test]
    public async Task Read_ASecondRead_ComesFromTheCache()
    {
        using var cache = AuditCache.Open(NewDb());
        ScannedFile[] readable = [_linked, _tagged, _untagged];
        TrackIdScan.Read(Ff, readable, jobs: 2, cache);

        // No ffprobe to be found: a file that had to be probed would throw.
        var second = TrackIdScan.Read(new FfmpegPaths("no-such-ffmpeg", "no-such-ffprobe"), readable, jobs: 2, cache);

        await Assert.That(second.Count).IsEqualTo(3);
        await Assert.That(second[_linked.Path]).IsEqualTo(new TrackIds(LinkedId, Isrc));
        await Assert.That(second[_tagged.Path]).IsEqualTo(new TrackIds(TaggedId, null));
        await Assert.That(second[_untagged.Path]).IsEqualTo(TrackIds.None);
    }

    // The cache answers for exactly the file it holds: the same size and modified time is answered
    // from it as it stands, another of either is another file, read again from its tags.
    [Test]
    [Arguments(0L, 0L, true)]
    [Arguments(1L, 0L, false)]
    [Arguments(0L, 1L, false)]
    public async Task Read_TheCacheIsKeyedOnTheFilesSizeAndModifiedTime(long sizeMore, long mtimeMore, bool fromCache)
    {
        using var cache = AuditCache.Open(NewDb());
        var planted = new TrackIds("2aBcDeFgHiJkLmNoPqRsTu", "GBXYZ2400001");
        cache.PutTrackIds(Target(_tagged), planted);
        var file = _tagged with { SizeBytes = _tagged.SizeBytes + sizeMore, MtimeTicks = _tagged.MtimeTicks + mtimeMore };

        await Assert.That(IdsOf(file, cache)).IsEqualTo(fromCache ? planted : new TrackIds(TaggedId, null));
    }

    [Test]
    public async Task Read_AnUntaggedFile_IsRememberedAsHavingNone()
    {
        using var cache = AuditCache.Open(NewDb());

        TrackIdScan.Read(Ff, [_untagged], jobs: 1, cache);

        await Assert.That(cache.TryGetTrackIds(Target(_untagged))).IsNotNull();
        await Assert.That(cache.TryGetTrackIds(Target(_untagged))).IsEqualTo(TrackIds.None);
    }

    [Test]
    public async Task Read_ReportsProgress_EndingOnTheLastFile()
    {
        var reports = new Collector();

        TrackIdScan.Read(Ff, [_linked, _tagged, _untagged, _broken], jobs: 2, progress: reports);

        // Four files stay under the throttle, so only the last one reports.
        await Assert.That(string.Join(", ", reports.All.Select(r => $"{r.Phase} {r.Done}/{r.Total}"))).IsEqualTo("Reading tags 4/4");
        await Assert.That(reports.All[^1].Fraction).IsEqualTo(1.0);
    }

    [Test]
    public async Task Read_ReportsEvery256thFile_AndTheLast()
    {
        // 300 files that are not there: each fails at the first check with no ffprobe started, so the
        // throttle shows without 300 probes. One job, so the reports come in the order they were made.
        ScannedFile[] missing =
        [
            .. Enumerable.Range(0, 300).Select(i => new ScannedFile(Path.Combine(_dir, $"missing {i}.flac"), _dir, null, null)),
        ];
        var reports = new Collector();

        var ids = TrackIdScan.Read(Ff, missing, jobs: 1, progress: reports);

        await Assert.That(ids.Count).IsEqualTo(300);
        await Assert.That(string.Join(", ", reports.All.Select(r => $"{r.Done}/{r.Total}"))).IsEqualTo("256/300, 300/300");
    }

    [Test]
    public async Task Read_ACancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(() => TrackIdScan.Read(Ff, [_linked, _tagged], jobs: 2, ct: cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Read_NoFiles_GivesNothing()
    {
        var reports = new Collector();

        var ids = TrackIdScan.Read(Ff, [], jobs: 2, progress: reports);

        await Assert.That(ids.Count).IsEqualTo(0);
        await Assert.That(reports.All).IsEmpty();
    }

    /// Appends each report under a lock as it arrives. Progress<T> would post them to the thread pool
    /// and could deliver them after Read returns.
    private sealed class Collector : IProgress<DupesProgress>
    {
        private readonly List<DupesProgress> _reports = [];

        public void Report(DupesProgress value)
        {
            lock (_reports) _reports.Add(value);
        }

        public DupesProgress[] All
        {
            get { lock (_reports) return [.. _reports]; }
        }
    }
}
