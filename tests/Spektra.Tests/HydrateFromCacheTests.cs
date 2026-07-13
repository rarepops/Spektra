using Spektra.Core;

namespace Spektra.Tests;

public sealed class HydrateFromCacheTests
{
    private static string NewTempDb() =>
        Path.Combine(Directory.CreateTempSubdirectory("spektra-hydrate").FullName,
            "audit-cache.db");

    private static readonly string Fixtures =
        Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;

    private static AuditTarget T(string file)
    {
        var info = new FileInfo(Path.Combine(Fixtures, file));
        return new AuditTarget(info.FullName, info.Length, info.LastWriteTimeUtc.Ticks);
    }

    [Test]
    public async Task Null_cache_returns_all_null()
    {
        AuditTarget[] targets = [T("sine-1khz.flac"), T("chirp.wav")];
        var entries = FolderAudit.HydrateFromCache(null, targets);

        await Assert.That(entries.Length).IsEqualTo(2);
        await Assert.That(entries.All(e => e is null)).IsTrue();
    }

    [Test]
    public async Task Hits_and_misses_are_positional()
    {
        using var cache = AuditCache.Open(NewTempDb());
        AuditTarget[] all = [T("sine-1khz.flac"), T("chirp.wav")];
        // Analyze only the first target so the cache holds exactly one row.
        FolderAudit.Run(Ff, [all[0]], jobs: 1, cache);

        var entries = FolderAudit.HydrateFromCache(cache, all);

        await Assert.That(entries[0]).IsNotNull();
        await Assert.That(entries[0]!.FromCache).IsTrue();
        await Assert.That(entries[1]).IsNull();
    }

    [Test]
    public async Task Stale_target_is_a_miss()
    {
        using var cache = AuditCache.Open(NewTempDb());
        var target = T("sine-1khz.flac");
        FolderAudit.Run(Ff, [target], jobs: 1, cache);

        var stale = target with { SizeBytes = 999 };
        var entries = FolderAudit.HydrateFromCache(cache, [stale]);

        await Assert.That(entries[0]).IsNull();
    }
}
