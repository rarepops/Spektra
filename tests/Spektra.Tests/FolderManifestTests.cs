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
    public async Task IsAudio_TracksTheAuditPipelinesExtensionSet()
    {
        var d = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(d, "song.FLAC"), "x");
            File.WriteAllText(Path.Combine(d, "cover.jpg"), "x");
            File.WriteAllText(Path.Combine(d, "README"), "x");
            var root = FolderManifest.Build(d, cache: null);
            var byName = root.Files.ToDictionary(f => f.Name, f => f.IsAudio);
            // Extension membership, same set FindAudioFiles walks: the flag is
            // "would the audit/viewer pipeline treat this as audio", so the
            // manifest's Open spectrogram can disable itself on everything else.
            await Assert.That(byName["song.FLAC"]).IsTrue();
            await Assert.That(byName["cover.jpg"]).IsFalse();
            await Assert.That(byName["README"]).IsFalse();
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
    public async Task ParseKinds_NormalizesSeparatorsDotsStarsAndCase()
    {
        var kinds = FolderManifest.ParseKinds(" *.FLAC, .jpg;nfo\tflac  ");
        await Assert.That(kinds.SequenceEqual(["flac", "jpg", "nfo"])).IsTrue();
        await Assert.That(FolderManifest.ParseKinds(null)).IsEmpty();
        await Assert.That(FolderManifest.ParseKinds("  . *. ")).IsEmpty();
    }

    [Test]
    public async Task Filter_KeepsMatches_PrunesEmptyFolders_RecountsRollups()
    {
        var d = NewDir();
        try
        {
            var disc = Path.Combine(d, "disc1");
            var art = Path.Combine(d, "art");
            Directory.CreateDirectory(disc);
            Directory.CreateDirectory(art);
            File.WriteAllText(Path.Combine(disc, "a.flac"), "x");
            File.WriteAllText(Path.Combine(disc, "a.nfo"), "x");
            File.WriteAllText(Path.Combine(art, "front.jpg"), "x");
            File.WriteAllText(Path.Combine(d, "notes.txt"), "x");
            var root = FolderManifest.Build(d, cache: null);

            var filtered = FolderManifest.Filter(root, ["flac", "jpg"]);
            // art and disc1 both keep a match; notes.txt and a.nfo are gone
            await Assert.That(filtered.Rollup).IsEqualTo("1 flac · 1 jpg");
            await Assert.That(filtered.Files).IsEmpty();
            await Assert.That(filtered.Folders.Select(f => f.Name).SequenceEqual(["art", "disc1"])).IsTrue();
            await Assert.That(filtered.Folders[1].Files.Single().Name).IsEqualTo("a.flac");
            await Assert.That(filtered.Folders[1].Rollup).IsEqualTo("1 flac");

            // a filter matching nothing keeps the root alone, honestly empty
            var none = FolderManifest.Filter(root, ["cue"]);
            await Assert.That(none.Folders).IsEmpty();
            await Assert.That(none.Files).IsEmpty();
            await Assert.That(none.Rollup).IsEqualTo("empty");

            // the original tree is untouched (filter is a pure view)
            await Assert.That(root.Files.Single().Name).IsEqualTo("notes.txt");
        }
        finally { Directory.Delete(d, recursive: true); }
    }

    [Test]
    public async Task Filter_MatchesChipKindOrRealExtension()
    {
        var d = NewDir();
        var dbPath = Path.Combine(d, "cache.db");
        try
        {
            // cached as an mp3 transcode: the chip says mp3, the name says .flac
            var audio = Path.Combine(d, "liar.flac");
            File.WriteAllBytes(audio, new byte[2048]);
            var info = new FileInfo(audio);
            using var cache = AuditCache.Open(dbPath);
            cache.Put(new AuditTarget(audio, info.Length, info.LastWriteTimeUtc.Ticks),
                Row("liar.flac", codec: "mp3", bandwidth: "Lossy", cutoffHz: 16_000), hasProblem: true);
            var root = FolderManifest.Build(d, cache);

            foreach (var query in (string[])["mp3", "flac"])
                await Assert.That(FolderManifest.Filter(root, [query])
                    .Files.Any(f => f.Name == "liar.flac")).IsTrue();
            await Assert.That(FolderManifest.Filter(root, ["wav"])
                .Files.Any(f => f.Name == "liar.flac")).IsFalse();
        }
        finally { Directory.Delete(d, recursive: true); }
    }

    [Test]
    public async Task TotalBytes_SumsRecursively_AndFilterRecomputesOverSurvivors()
    {
        var d = NewDir();
        try
        {
            var sub = Path.Combine(d, "disc1");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "a.flac"), "12345"); // 5 bytes
            File.WriteAllText(Path.Combine(d, "notes.nfo"), "abc");  // 3 bytes
            var root = FolderManifest.Build(d, cache: null);
            await Assert.That(root.TotalBytes).IsEqualTo(8);
            await Assert.That(root.Folders.Single().TotalBytes).IsEqualTo(5);

            // Filtering away the nfo leaves only the flac's bytes at the root.
            var filtered = FolderManifest.Filter(root, ["flac"]);
            await Assert.That(filtered.TotalBytes).IsEqualTo(5);
        }
        finally { Directory.Delete(d, recursive: true); }
    }

    [Test]
    public async Task Build_PreCancelledToken_ThrowsInsteadOfListing()
    {
        var d = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(d, "a.txt"), "x");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.That(() => FolderManifest.Build(d, cache: null, cts.Token))
                .Throws<OperationCanceledException>();
        }
        finally { Directory.Delete(d, recursive: true); }
    }

    [Test]
    public async Task Filter_KeepsUnreadableNodes()
    {
        var unreadable = new ManifestFolder("locked", @"C:\locked", [], [], 0, "unreadable", Unreadable: true);
        var root = new ManifestFolder("lib", @"C:\lib", [unreadable],
            [new ManifestFile("a.txt", @"C:\lib\a.txt", "txt", null, 1, IsAudio: false)], 1, "1 txt", Unreadable: false);
        var filtered = FolderManifest.Filter(root, ["flac"]);
        // no txt match, but the unreadable child stays: it might hold matches
        await Assert.That(filtered.Files).IsEmpty();
        await Assert.That(filtered.Folders.Single().Unreadable).IsTrue();
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
