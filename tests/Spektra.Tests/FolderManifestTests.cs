using Spektra.Core;

namespace Spektra.Tests;

public sealed class FolderManifestTests
{
    private static string NewDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "spektra-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static AuditRow Row(string file, string codec = "flac", string bandwidth = "Lossless",
        double? cutoffHz = null, string integrity = "Ok") =>
        new(file, codec, 44100, 2, 900_000, 60, bandwidth, cutoffHz, integrity, 0, 0, false, null);

    [Test]
    public async Task SortsFoldersFirstThenFiles_Alphabetically()
    {
        var d = NewDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(d, "b-sub"));
            Directory.CreateDirectory(Path.Combine(d, "A-sub"));
            File.WriteAllText(Path.Combine(d, "zeta.txt"), "x");
            File.WriteAllText(Path.Combine(d, "Alpha.txt"), "x");
            var root = FolderManifest.Build(d, cache: null);
            await Assert.That(root.Folders.Select(f => f.Name).SequenceEqual(["A-sub", "b-sub"])).IsTrue();
            await Assert.That(root.Files.Select(f => f.Name).SequenceEqual(["Alpha.txt", "zeta.txt"])).IsTrue();
        }
        finally { Directory.Delete(d, recursive: true); }
    }

    [Test]
    public async Task Kind_IsLowercasedExtension_NoneWhenMissing_AudioDetectionCaseInsensitive()
    {
        var d = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(d, "cover.JPG"), "x");
            File.WriteAllText(Path.Combine(d, "README"), "x");
            File.WriteAllText(Path.Combine(d, "song.FLAC"), "x");
            var root = FolderManifest.Build(d, cache: null);
            var kinds = root.Files.ToDictionary(f => f.Name, f => f.Kind);
            await Assert.That(kinds["cover.JPG"]).IsEqualTo("jpg");
            await Assert.That(kinds["README"]).IsEqualTo("none");
            // .FLAC is audio but uncached: plain extension chip, no severity
            await Assert.That(kinds["song.FLAC"]).IsEqualTo("flac");
            await Assert.That(root.Files.Single(f => f.Name == "song.FLAC").Severity).IsNull();
        }
        finally { Directory.Delete(d, recursive: true); }
    }

    [Test]
    public async Task Rollup_IsRecursive_CountDescendingThenName_EmptyWhenEmpty()
    {
        var d = NewDir();
        try
        {
            var sub = Path.Combine(d, "disc1");
            Directory.CreateDirectory(sub);
            Directory.CreateDirectory(Path.Combine(d, "hollow"));
            File.WriteAllText(Path.Combine(sub, "a.flac"), "x");
            File.WriteAllText(Path.Combine(sub, "b.flac"), "x");
            File.WriteAllText(Path.Combine(d, "cover.jpg"), "x");
            File.WriteAllText(Path.Combine(d, "notes.nfo"), "x");
            var root = FolderManifest.Build(d, cache: null);
            // 2 flac beats 1 jpg / 1 nfo; ties order by name (jpg before nfo)
            await Assert.That(root.Rollup).IsEqualTo("2 flac · 1 jpg · 1 nfo");
            await Assert.That(root.Folders.Single(f => f.Name == "disc1").Rollup).IsEqualTo("2 flac");
            await Assert.That(root.Folders.Single(f => f.Name == "hollow").Rollup).IsEqualTo("empty");
        }
        finally { Directory.Delete(d, recursive: true); }
    }

    [Test]
    public async Task CacheHit_DecoratesWithCodecAndSeverity()
    {
        var d = NewDir();
        var dbPath = Path.Combine(d, "cache.db");
        try
        {
            // uppercase extension: exercises the cache-decoration branch's
            // case-insensitive audio detection, not just the lowercase path
            var audio = Path.Combine(d, "MISLABELED.FLAC");
            File.WriteAllBytes(audio, new byte[2048]);
            var info = new FileInfo(audio);
            var target = new AuditTarget(audio, info.Length, info.LastWriteTimeUtc.Ticks);
            using (var cache = AuditCache.Open(dbPath))
            {
                // cached truth: it is really an mp3 transcode hiding in a flac
                cache.Put(target, Row(Path.GetFileName(audio), codec: "mp3", bandwidth: "Lossy", cutoffHz: 16_000), hasProblem: true);
                var root = FolderManifest.Build(d, cache);
                var file = root.Files.Single(f => f.Name == "MISLABELED.FLAC");
                await Assert.That(file.Kind).IsEqualTo("mp3");
                await Assert.That(file.Severity).IsEqualTo(RowSeverity.Problem);

                // pins the enum-to-string severity conversion in ManifestRow
                var rows = FolderManifest.ToRows(root);
                var row = rows.Single(r => r.Name == "MISLABELED.FLAC");
                await Assert.That(row.Severity).IsEqualTo("Problem");
            }
        }
        finally { Directory.Delete(d, recursive: true); }
    }

    [Test]
    public async Task StaleCacheEntry_FallsBackToExtensionChip()
    {
        var d = NewDir();
        var dbPath = Path.Combine(d, "cache.db");
        try
        {
            var audio = Path.Combine(d, "song.flac");
            File.WriteAllBytes(audio, new byte[1024]);
            var info = new FileInfo(audio);
            using (var cache = AuditCache.Open(dbPath))
            {
                cache.Put(new AuditTarget(audio, info.Length, info.LastWriteTimeUtc.Ticks),
                    Row("song.flac", codec: "mp3"), hasProblem: false);
                // rewrite the file: size and mtime change, the entry goes stale
                File.WriteAllBytes(audio, new byte[4096]);
                var root = FolderManifest.Build(d, cache);
                // AuditCache runs WAL mode: cache.db-shm/-wal sit alongside cache.db
                // in the same listed directory, so filter by name rather than Single().
                var file = root.Files.Single(f => f.Name == "song.flac");
                await Assert.That(file.Kind).IsEqualTo("flac");
                await Assert.That(file.Severity).IsNull();
                await Assert.That(file.SizeBytes).IsEqualTo(4096L);
            }
        }
        finally { Directory.Delete(d, recursive: true); }
    }

    [Test]
    public async Task UnenumerableRoot_YieldsSingleUnreadableNode()
    {
        var missing = Path.Combine(Path.GetTempPath(), "spektra-manifest-missing-" + Guid.NewGuid().ToString("N"));
        var root = FolderManifest.Build(missing, cache: null);
        await Assert.That(root.Unreadable).IsTrue();
        await Assert.That(root.Rollup).IsEqualTo("unreadable");
        await Assert.That(root.Folders).IsEmpty();
        await Assert.That(root.Files).IsEmpty();
    }

    [Test]
    public async Task MalformedRoot_YieldsUnreadableNode_NotAThrow()
    {
        var root = FolderManifest.Build("", cache: null);
        await Assert.That(root.Unreadable).IsTrue();
        await Assert.That(root.Rollup).IsEqualTo("unreadable");
        await Assert.That(root.Folders).IsEmpty();
        await Assert.That(root.Files).IsEmpty();
    }

    [Test]
    public async Task ToRows_FlattensInDisplayOrder_SeverityAsString()
    {
        var d = NewDir();
        try
        {
            var sub = Path.Combine(d, "art");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "front.jpg"), "x");
            File.WriteAllText(Path.Combine(d, "track.mp3"), "x");
            var root = FolderManifest.Build(d, cache: null);
            var rows = FolderManifest.ToRows(root);
            // subfolder contents come before this folder's own files, matching the tree top to bottom
            await Assert.That(rows.Select(r => r.Name).SequenceEqual(["front.jpg", "track.mp3"])).IsTrue();
            await Assert.That(rows[0].Kind).IsEqualTo("jpg");
            await Assert.That(rows[0].Severity).IsNull();
            await Assert.That(rows[1].Path).IsEqualTo(Path.Combine(d, "track.mp3"));
        }
        finally { Directory.Delete(d, recursive: true); }
    }
}
