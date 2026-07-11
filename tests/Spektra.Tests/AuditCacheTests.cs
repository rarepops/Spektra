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
    public async Task Put_TryGet_RoundTrips()
    {
        using var cache = AuditCache.Open(DbPath);
        cache.Put(Target(), Row(), hasProblem: true);

        var entry = cache.TryGet(Target());

        await Assert.That(entry).IsNotNull();
        await Assert.That(entry!.FromCache).IsTrue();
        await Assert.That(entry.HasProblem).IsTrue();
        await Assert.That(entry.Target).IsEqualTo(Target());
        await Assert.That(entry.Row).IsEqualTo(Row());
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
}
