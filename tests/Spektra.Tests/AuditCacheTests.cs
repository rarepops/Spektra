using Microsoft.Data.Sqlite;
using Spektra.Core;

namespace Spektra.Tests;

public sealed class AuditCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("spektra-cache").FullName;
    private string DbPath => Path.Combine(_dir, "audit-cache.db");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static AuditTarget Target(string path = @"C:\music\a.flac", long size = 1234, long mtime = 5678) =>
        new(path, size, mtime);

    private static AuditRow Row(string file = "a.flac", string? error = null) => new(
        file, "flac", 44100, 2, 900_000, 231.5, "Lossless", 22050, "Ok", 0, 0, false, error);

    [Test]
    public async Task TryGet_OnEmptyCache_ReturnsNull()
    {
        using var cache = AuditCache.Open(DbPath);
        await Assert.That(cache.TryGet(Target())).IsNull();
    }

    [Test]
    public async Task TryOpen_ValidPath_ReturnsCache_NoError()
    {
        using var cache = AuditCache.TryOpen(DbPath, out var error);
        await Assert.That(cache).IsNotNull();
        await Assert.That(error).IsNull();
    }

    [Test]
    public async Task TryOpen_UnopenablePath_ReturnsNull_WithReason()
    {
        // A directory sitting where the db file should be: SQLite cannot open
        // it, so TryOpen must report the reason and hand back null (the caller
        // runs uncached) instead of throwing.
        var asDir = Path.Combine(_dir, "as-a-directory.db");
        Directory.CreateDirectory(asDir);

        var cache = AuditCache.TryOpen(asDir, out var error);

        await Assert.That(cache).IsNull();
        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task Put_TryGet_RoundTrips()
    {
        using var cache = AuditCache.Open(DbPath);
        var upsampled = Row() with { Bandwidth = "Upsampled" }; // a problem by the rule
        cache.Put(Target(), upsampled, hasProblem: true);

        var entry = cache.TryGet(Target());

        await Assert.That(entry).IsNotNull();
        await Assert.That(entry!.FromCache).IsTrue();
        await Assert.That(entry.HasProblem).IsTrue();
        await Assert.That(entry.Target).IsEqualTo(Target());
        await Assert.That(entry.Row).IsEqualTo(upsampled);
    }

    [Test]
    public async Task TryGet_DerivesHasProblem_FromTheRow()
    {
        // The stored flag is what the build that analyzed the file thought. The
        // rule is applied to the row on read instead, so a rule change reaches
        // cached rows without re-analysis.
        using var cache = AuditCache.Open(DbPath);
        var renamed = Target(@"C:\music\renamed.flac");
        // What a build before the renamed-file rule wrote: an mp3 under a .flac name, stored clean.
        cache.Put(renamed, Row() with { File = "renamed.flac", Codec = "mp3" }, hasProblem: false);
        cache.Put(Target(), Row(), hasProblem: true);

        await Assert.That(cache.TryGet(renamed)!.HasProblem).IsTrue();
        await Assert.That(cache.TryGet(Target())!.HasProblem).IsFalse();
    }

    [Test]
    [Arguments(9999L, 5678L)]  // size changed
    [Arguments(1234L, 9999L)]  // mtime changed
    public async Task TryGet_IsStale_WhenIdentityChanges(long size, long mtime)
    {
        using var cache = AuditCache.Open(DbPath);
        cache.Put(Target(), Row(), hasProblem: false);
        await Assert.That(cache.TryGet(Target(size: size, mtime: mtime))).IsNull();
    }

    [Test]
    public async Task TryGet_IsStale_OnAnalysisVersionMismatch()
    {
        using (var cache = AuditCache.Open(DbPath))
            cache.Put(Target(), Row(), hasProblem: false);

        using (var db = new SqliteConnection($"Data Source={DbPath};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE files SET analysis_version = analysis_version - 1";
            cmd.ExecuteNonQuery();
        }

        using var reopened = AuditCache.Open(DbPath);
        await Assert.That(reopened.TryGet(Target())).IsNull();
    }

    [Test]
    public async Task TryGet_OnCorruptRowJson_ReturnsNull()
    {
        using (var cache = AuditCache.Open(DbPath))
            cache.Put(Target(), Row(), hasProblem: false);

        // A partially written / corrupt row (not a corrupt DB file): the row
        // identity still matches, but its JSON no longer parses.
        using (var db = new SqliteConnection($"Data Source={DbPath};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE files SET row_json = 'not valid json'";
            cmd.ExecuteNonQuery();
        }

        using var reopened = AuditCache.Open(DbPath);
        await Assert.That(reopened.TryGet(Target())).IsNull();
    }

    [Test]
    public async Task PruneFolder_DeletesOnlyStaleRowsUnderTheFolder()
    {
        using var cache = AuditCache.Open(DbPath);
        cache.Put(Target(@"C:\music\keep.flac"), Row("keep.flac"), false);
        cache.Put(Target(@"C:\music\gone.flac"), Row("gone.flac"), false);
        cache.Put(Target(@"D:\other\safe.flac"), Row("safe.flac"), false);

        cache.PruneFolder(@"C:\music", [@"C:\music\keep.flac"]);

        await Assert.That(cache.TryGet(Target(@"C:\music\keep.flac"))).IsNotNull();
        await Assert.That(cache.TryGet(Target(@"C:\music\gone.flac"))).IsNull();
        await Assert.That(cache.TryGet(Target(@"D:\other\safe.flac"))).IsNotNull();
    }

    [Test]
    public async Task Open_OnCorruptFile_RecreatesEmpty_AndWorks()
    {
        File.WriteAllBytes(DbPath, [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03]);

        using var cache = AuditCache.Open(DbPath);

        await Assert.That(cache.TryGet(Target())).IsNull();
        cache.Put(Target(), Row(), false);
        await Assert.That(cache.TryGet(Target())).IsNotNull();
    }

    [Test]
    [Arguments(5, false)]   // SQLITE_BUSY: another instance has the file
    [Arguments(6, false)]   // SQLITE_LOCKED
    [Arguments(10, false)]  // SQLITE_IOERR: flaky disk
    [Arguments(13, false)]  // SQLITE_FULL
    [Arguments(14, false)]  // SQLITE_CANTOPEN
    [Arguments(11, true)]   // SQLITE_CORRUPT
    [Arguments(26, true)]   // SQLITE_NOTADB
    public async Task IsCorruptionError_RecreatesOnlyForBadFiles(int sqliteCode, bool expected)
    {
        await Assert.That(AuditCache.IsCorruptionError(new SqliteException("boom", sqliteCode)))
            .IsEqualTo(expected);
    }

    [Test]
    public async Task Put_InParallel_UpsertsAllRows()
    {
        using var cache = AuditCache.Open(DbPath);
        Parallel.For(0, 100, i =>
            cache.Put(Target($@"C:\music\{i}.flac"), Row($"{i}.flac"), false));
        for (var i = 0; i < 100; i++)
            await Assert.That(cache.TryGet(Target($@"C:\music\{i}.flac"))).IsNotNull();
    }

    [Test]
    public async Task Fingerprint_RoundTrips_AndStalesOnIdentityChange()
    {
        using var cache = AuditCache.Open(DbPath);
        var target = new AuditTarget(@"C:\m\a.flac", 100, 200);
        cache.PutFingerprint(target, new Fingerprint(8, [1u, 2u, 3u]), 12.5, "Artist", "Title");

        var hit = cache.TryGetFingerprint(target);
        await Assert.That(hit).IsNotNull();
        await Assert.That(hit!.Fingerprint.Words.SequenceEqual(new uint[] { 1, 2, 3 })).IsTrue();
        await Assert.That(hit.DurationSeconds).IsEqualTo(12.5);
        await Assert.That(hit.Artist).IsEqualTo("Artist");
        await Assert.That(hit.Title).IsEqualTo("Title");
        await Assert.That(cache.TryGetFingerprint(target with { SizeBytes = 101 })).IsNull();
        await Assert.That(cache.TryGetFingerprint(target with { MtimeTicks = 201 })).IsNull();
    }

    [Test]
    public async Task PruneFolder_AlsoPrunesFingerprints()
    {
        using var cache = AuditCache.Open(DbPath);
        var dead = new AuditTarget(@"C:\m\dead.mp3", 1, 1);
        var live = new AuditTarget(@"C:\m\live.mp3", 1, 1);
        var outside = new AuditTarget(@"C:\other\keep.mp3", 1, 1);
        foreach (var t in new[] { dead, live, outside })
            cache.PutFingerprint(t, new Fingerprint(8, [7u]), 1, null, null);

        cache.PruneFolder(@"C:\m", [live.Path]);

        await Assert.That(cache.TryGetFingerprint(dead)).IsNull();
        await Assert.That(cache.TryGetFingerprint(live)).IsNotNull();
        await Assert.That(cache.TryGetFingerprint(outside)).IsNotNull();
    }

    [Test]
    public async Task Open_SetsSynchronousNormal()
    {
        // Pinned because dropping the pragma breaks nothing visible: every other
        // test here still passes, writes just go back to ~1.5 ms each (a flush
        // per row). 1 = NORMAL, which under WAL risks only the last transactions
        // on a power cut - acceptable for a cache that is disposable by design.
        using var cache = AuditCache.Open(DbPath);
        await Assert.That(cache.SynchronousMode()).IsEqualTo(1);
    }

    private static readonly TrackIds SomeIds = new("0aBcDeFgHiJkLmNoPqRsTu", "USRC17607839");

    [Test]
    public async Task TrackIds_RoundTrip()
    {
        using var cache = AuditCache.Open(DbPath);
        var both = new AuditTarget(@"C:\m\both.flac", 100, 200);
        var spotifyOnly = new AuditTarget(@"C:\m\spotify.flac", 100, 200);
        var isrcOnly = new AuditTarget(@"C:\m\isrc.flac", 100, 200);
        var spotify = new TrackIds("1zYxWvUtSrQpOnMlKjIhGf", null);
        var isrc = new TrackIds(null, "GBXYZ2400001");
        cache.PutTrackIds(both, SomeIds);
        cache.PutTrackIds(spotifyOnly, spotify);
        cache.PutTrackIds(isrcOnly, isrc);

        // Each column comes back in its own field, and a null stays null.
        await Assert.That(cache.TryGetTrackIds(both)).IsEqualTo(SomeIds);
        await Assert.That(cache.TryGetTrackIds(spotifyOnly)).IsEqualTo(spotify);
        await Assert.That(cache.TryGetTrackIds(isrcOnly)).IsEqualTo(isrc);
        await Assert.That(cache.TryGetTrackIds(new AuditTarget(@"C:\m\never put.flac", 100, 200))).IsNull();

        // A second put for the same path replaces the row, identity and all.
        var changed = both with { SizeBytes = 101, MtimeTicks = 201 };
        cache.PutTrackIds(changed, isrc);
        await Assert.That(cache.TryGetTrackIds(changed)).IsEqualTo(isrc);
        await Assert.That(cache.TryGetTrackIds(both)).IsNull();
    }

    [Test]
    public async Task TrackIds_NoneIsRemembered_AsNotNull()
    {
        using var cache = AuditCache.Open(DbPath);
        cache.PutTrackIds(Target(), TrackIds.None);

        var hit = cache.TryGetTrackIds(Target());

        await Assert.That(hit).IsNotNull();
        await Assert.That(hit).IsEqualTo(TrackIds.None);
        await Assert.That(hit!.IsEmpty).IsTrue();
    }

    [Test]
    [Arguments(9999L, 5678L)]  // size changed
    [Arguments(1234L, 9999L)]  // mtime changed
    public async Task TrackIds_AreStale_WhenIdentityChanges(long size, long mtime)
    {
        using var cache = AuditCache.Open(DbPath);
        cache.PutTrackIds(Target(), SomeIds);

        // The exact file hits, so the miss below is the identity's doing.
        await Assert.That(cache.TryGetTrackIds(Target())).IsEqualTo(SomeIds);
        await Assert.That(cache.TryGetTrackIds(Target(size: size, mtime: mtime))).IsNull();
    }

    [Test]
    public async Task TrackIds_AreStale_OnVersionMismatch()
    {
        using (var cache = AuditCache.Open(DbPath))
        {
            cache.PutTrackIds(Target(), SomeIds);
            await Assert.That(cache.TryGetTrackIds(Target())).IsEqualTo(SomeIds);
        }

        using (var db = new SqliteConnection($"Data Source={DbPath};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE track_ids SET ids_version = ids_version - 1";
            cmd.ExecuteNonQuery();
        }

        using var reopened = AuditCache.Open(DbPath);
        await Assert.That(reopened.TryGetTrackIds(Target())).IsNull();
    }

    [Test]
    public async Task TrackIds_AnOlderCacheGainsTheTable()
    {
        // The cache an older Spektra made: the audit and fingerprint tables, with a row in each, and
        // no table of IDs.
        var target = new AuditTarget(@"C:\m\old.flac", 100, 200);
        using (var older = AuditCache.Open(DbPath))
        {
            older.Put(target, Row("old.flac"), hasProblem: false);
            older.PutFingerprint(target, new Fingerprint(8, [1u, 2u, 3u]), 12.5, "Artist", "Title");
        }

        using (var db = new SqliteConnection($"Data Source={DbPath};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DROP TABLE IF EXISTS track_ids";
            cmd.ExecuteNonQuery();
        }

        using var cache = AuditCache.Open(DbPath);

        // The table is there again and empty, and what the older cache held is still held.
        await Assert.That(cache.TryGetTrackIds(target)).IsNull();
        cache.PutTrackIds(target, SomeIds);
        await Assert.That(cache.TryGetTrackIds(target)).IsEqualTo(SomeIds);
        await Assert.That(cache.TryGet(target)).IsNotNull();
        await Assert.That(cache.TryGetFingerprint(target)).IsNotNull();
    }

    [Test]
    public async Task PruneFolder_AlsoPrunesTrackIds()
    {
        using var cache = AuditCache.Open(DbPath);
        var dead = new AuditTarget(@"C:\m\dead.mp3", 1, 1);
        var live = new AuditTarget(@"C:\m\live.mp3", 1, 1);
        var outside = new AuditTarget(@"C:\other\keep.mp3", 1, 1);
        foreach (var t in new[] { dead, live, outside })
            cache.PutTrackIds(t, SomeIds);

        cache.PruneFolder(@"C:\m", [live.Path]);

        await Assert.That(cache.TryGetTrackIds(dead)).IsNull();
        await Assert.That(cache.TryGetTrackIds(live)).IsNotNull();
        await Assert.That(cache.TryGetTrackIds(outside)).IsNotNull();
    }
}
