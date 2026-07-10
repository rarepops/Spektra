using Microsoft.Data.Sqlite;
using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public sealed class AuditCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("spektra-cache").FullName;
    private string DbPath => Path.Combine(_dir, "audit-cache.db");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static AuditTarget Target(string path = @"C:\music\a.flac", long size = 1234, long mtime = 5678) =>
        new(path, size, mtime);

    private static AuditRow Row(string file = "a.flac", string? error = null) => new(
        file, "flac", 44100, 900_000, 231.5, "Lossless", 22050, "Ok", 0, 0, false, error);

    [Fact]
    public void TryGet_OnEmptyCache_ReturnsNull()
    {
        using var cache = AuditCache.Open(DbPath);
        Assert.Null(cache.TryGet(Target()));
    }

    [Fact]
    public void Put_TryGet_RoundTrips()
    {
        using var cache = AuditCache.Open(DbPath);
        cache.Put(Target(), Row(), hasProblem: true);

        var entry = cache.TryGet(Target());

        Assert.NotNull(entry);
        Assert.True(entry.FromCache);
        Assert.True(entry.HasProblem);
        Assert.Equal(Target(), entry.Target);
        Assert.Equal(Row(), entry.Row);
    }

    [Theory]
    [InlineData(9999L, 5678L)]  // size changed
    [InlineData(1234L, 9999L)]  // mtime changed
    public void TryGet_IsStale_WhenIdentityChanges(long size, long mtime)
    {
        using var cache = AuditCache.Open(DbPath);
        cache.Put(Target(), Row(), hasProblem: false);
        Assert.Null(cache.TryGet(Target(size: size, mtime: mtime)));
    }

    [Fact]
    public void TryGet_IsStale_OnAnalysisVersionMismatch()
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
        Assert.Null(reopened.TryGet(Target()));
    }

    [Fact]
    public void PruneFolder_DeletesOnlyStaleRowsUnderTheFolder()
    {
        using var cache = AuditCache.Open(DbPath);
        cache.Put(Target(@"C:\music\keep.flac"), Row("keep.flac"), false);
        cache.Put(Target(@"C:\music\gone.flac"), Row("gone.flac"), false);
        cache.Put(Target(@"D:\other\safe.flac"), Row("safe.flac"), false);

        cache.PruneFolder(@"C:\music", [@"C:\music\keep.flac"]);

        Assert.NotNull(cache.TryGet(Target(@"C:\music\keep.flac")));
        Assert.Null(cache.TryGet(Target(@"C:\music\gone.flac")));
        Assert.NotNull(cache.TryGet(Target(@"D:\other\safe.flac")));
    }

    [Fact]
    public void Open_OnCorruptFile_RecreatesEmpty_AndWorks()
    {
        File.WriteAllBytes(DbPath, [0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03]);

        using var cache = AuditCache.Open(DbPath);

        Assert.Null(cache.TryGet(Target()));
        cache.Put(Target(), Row(), false);
        Assert.NotNull(cache.TryGet(Target()));
    }

    [Fact]
    public void Put_InParallel_UpsertsAllRows()
    {
        using var cache = AuditCache.Open(DbPath);
        Parallel.For(0, 100, i =>
            cache.Put(Target($@"C:\music\{i}.flac"), Row($"{i}.flac"), false));
        for (var i = 0; i < 100; i++)
            Assert.NotNull(cache.TryGet(Target($@"C:\music\{i}.flac")));
    }
}
