using Spektra.Core;

namespace Spektra.Tests;

public sealed class DuplicateScanTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static string P(string file) => Path.Combine(Fixtures, file);

    [Test]
    public async Task Run_FindsRenamedCopiesAcrossRoots_AndRanksTheLosslessOriginalFirst()
    {
        var rootA = Directory.CreateTempSubdirectory("dupes-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("dupes-b").FullName;
        try
        {
            // The true pair is the in-band tonal fixture plus its 128k encode
            // under an unrelated name (renames cannot hide a copy). chirp and
            // noise are strangers on both sides and must stay ungrouped: the
            // chirp/64k pair that used to play the duplicate here rode the
            // sparse-word floor that chance-corrected similarity removed.
            File.Copy(P("tones-a.wav"), Path.Combine(rootA, "Tones Song.wav"));
            File.Copy(P("chirp.wav"), Path.Combine(rootA, "chirp.wav"));
            File.Copy(P("tones-a-128.mp3"), Path.Combine(rootB, "completely unrelated name.mp3"));
            File.Copy(P("noise.wav"), Path.Combine(rootB, "noise.wav"));

            var result = DuplicateScan.Run(Ff, [rootA, rootB], jobs: 2, minDurationSeconds: 0);

            await Assert.That(result.FilesScanned).IsEqualTo(4);
            await Assert.That(result.NotAnalyzed).IsEmpty();
            await Assert.That(result.Groups).HasSingleItem();

            var g = result.Groups[0];
            await Assert.That(g.Group.Members.Count).IsEqualTo(2);
            await Assert.That(g.Quality.Winners.Single()).IsEqualTo(Path.Combine(rootA, "Tones Song.wav"));
            await Assert.That(g.Group.Members.Single(m => m.Path.EndsWith("name.mp3")).FoundByAudio).IsTrue();
            await Assert.That(g.ReclaimableBytes).IsGreaterThan(0);
            await Assert.That(result.ReclaimableBytes).IsEqualTo(g.ReclaimableBytes);
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Test]
    public async Task Run_ReportsUsableFilesThatMatchedNothing()
    {
        var rootA = Directory.CreateTempSubdirectory("diff-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("diff-b").FullName;
        try
        {
            // One track both folders hold, and one only A holds. "Only in this
            // folder" is the commonest kind of difference a folder diff shows,
            // and it is precisely what a duplicate scan discards today.
            File.Copy(P("tones-a.wav"), Path.Combine(rootA, "shared.wav"));
            File.Copy(P("tones-a-128.mp3"), Path.Combine(rootB, "shared.mp3"));
            File.Copy(P("noise.wav"), Path.Combine(rootA, "only-in-a.wav"));

            var result = DuplicateScan.Run(Ff, [rootA, rootB], jobs: 2, minDurationSeconds: 0);

            await Assert.That(result.Groups).HasSingleItem();
            await Assert.That(result.Unpaired).HasSingleItem();

            var lone = result.Unpaired[0];
            await Assert.That(Path.GetFileName(lone.Path)).IsEqualTo("only-in-a.wav");
            await Assert.That(lone.Root).IsEqualTo(rootA);
            await Assert.That(lone.SizeBytes).IsGreaterThan(0);
            await Assert.That(lone.Row.Bandwidth).IsNotNull();
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Test]
    public async Task Run_AttributesUnpairedFilesToTheDeepestRootHoldingThem()
    {
        // "Compare my library against the new rips folder inside it" is a
        // natural way to reach the folder diff, and nested roots get there:
        // AddRoot turns away only an exact repeat, and the CLI takes any paths
        // at all. Attribution is by longest matching root, NOT by whose walk
        // reached the file first, and the outer root is listed first here so
        // the two orders disagree. The split view builds one column per root
        // from this field, so filing the inner folder's file under the outer
        // one puts it in the wrong column, in plain sight.
        var outer = Directory.CreateTempSubdirectory("diff-outer").FullName;
        var inner = Directory.CreateDirectory(Path.Combine(outer, "New Rips")).FullName;
        try
        {
            File.Copy(P("chirp.wav"), Path.Combine(outer, "outer-only.wav"));
            File.Copy(P("noise.wav"), Path.Combine(inner, "inner-only.wav"));

            var result = DuplicateScan.Run(Ff, [outer, inner], jobs: 2, minDurationSeconds: 0);

            // The inner file sits under both roots and must still be one file:
            // analyzed twice it would pair with itself and read as a duplicate.
            await Assert.That(result.FilesScanned).IsEqualTo(2);
            await Assert.That(result.NotAnalyzed).IsEmpty();
            await Assert.That(result.Groups).IsEmpty();
            await Assert.That(result.Unpaired.Count).IsEqualTo(2);

            await Assert.That(result.Unpaired.Single(u => u.Path.EndsWith("inner-only.wav")).Root)
                .IsEqualTo(inner);
            await Assert.That(result.Unpaired.Single(u => u.Path.EndsWith("outer-only.wav")).Root)
                .IsEqualTo(outer);
        }
        finally
        {
            Directory.Delete(outer, recursive: true);
        }
    }

    [Test]
    public async Task IsSameTrack_FollowsSamenessTier_NotQuality()
    {
        // The two ideas are crossed on purpose. The confident group has a
        // clear quality winner and is still the same track; the weak group is
        // a perfect quality tie and is still not. A predicate written against
        // QualityRanking passes neither case, which is the point: whether a
        // FLAC beats an MP3 is the ordinary view's question, not a diff's.
        var confident = Report(tier: "High", members: ["a.flac", "a.mp3"], winners: ["a.flac"]);
        var weak = Report(tier: "Medium", members: ["a.flac", "b.flac"], winners: ["a.flac", "b.flac"]);

        await Assert.That(confident.IsSameTrack).IsTrue();
        await Assert.That(weak.IsSameTrack).IsFalse();
    }

    private static DupesGroupReport Report(string tier, string[] members, string[] winners) =>
        new(new DuplicateGroup(1, "label", tier,
                [.. members.Select(m => new DuplicateMember(m, 0.99, tier, false))]),
            new QualityRanking(winners, "High", "reason", members),
            new Dictionary<string, AuditRow>(),
            new Dictionary<string, long>(),
            ReclaimableBytes: 0);

    [Test]
    public async Task Run_ShortFiles_LandInNotAnalyzed_NotInGroups()
    {
        var root = Directory.CreateTempSubdirectory("dupes-short").FullName;
        try
        {
            File.Copy(P("chirp.wav"), Path.Combine(root, "a.wav"));
            File.Copy(P("chirp.wav"), Path.Combine(root, "b.wav"));
            var result = DuplicateScan.Run(Ff, [root], jobs: 1, minDurationSeconds: 3600);
            await Assert.That(result.Groups).IsEmpty();
            await Assert.That(result.NotAnalyzed.Count).IsEqualTo(2);
            await Assert.That(result.NotAnalyzed[0].Reason).Contains("shorter");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task Run_NamesTheTwinOfAnUnpairedFileUnderAnotherRoot()
    {
        var rootA = Directory.CreateTempSubdirectory("twin-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("twin-b").FullName;
        try
        {
            // Same name on both sides, different audio: neither file can pair
            // by ear, and each must still be told the other folder has its
            // track. A third file with a name of its own gets no twin.
            File.Copy(P("chirp.wav"), Path.Combine(rootA, "Shared Title.wav"));
            File.Copy(P("noise.wav"), Path.Combine(rootB, "Shared Title.wav"));
            File.Copy(P("tones-a.wav"), Path.Combine(rootA, "Lonely.wav"));

            var result = DuplicateScan.Run(Ff, [rootA, rootB], jobs: 2, minDurationSeconds: 0);

            await Assert.That(result.Groups).IsEmpty();
            await Assert.That(result.Unpaired.Count).IsEqualTo(3);
            var inA = result.Unpaired.Single(u => u.Path == Path.Combine(rootA, "Shared Title.wav"));
            var inB = result.Unpaired.Single(u => u.Path == Path.Combine(rootB, "Shared Title.wav"));
            await Assert.That(inA.TwinPath).IsEqualTo(inB.Path);
            await Assert.That(inB.TwinPath).IsEqualTo(inA.Path);
            await Assert.That(result.Unpaired.Single(u => u.Path.EndsWith("Lonely.wav")).TwinPath).IsNull();
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Test]
    public async Task Run_AboveTheAllPairsCutoff_StillFindsTheCopies()
    {
        // 66 usable files is past the 64-file cutoff below which every pair
        // is matched directly, so candidates come from FingerprintIndex
        // instead. Nothing else in this suite reaches that branch.
        var root = Directory.CreateTempSubdirectory("dupes-index").FullName;
        try
        {
            for (var i = 0; i < 33; i++)
            {
                File.Copy(P("tones-a.wav"), Path.Combine(root, $"tones-{i:00}.wav"));
                File.Copy(P("noise.wav"), Path.Combine(root, $"noise-{i:00}.wav"));
            }

            var result = DuplicateScan.Run(Ff, [root], jobs: 4, minDurationSeconds: 0);

            await Assert.That(result.FilesScanned).IsEqualTo(66);
            await Assert.That(result.NotAnalyzed).IsEmpty();
            await Assert.That(result.Unpaired).IsEmpty();
            await Assert.That(result.Groups.Count).IsEqualTo(2);
            await Assert.That(result.Groups.All(g => g.Group.Members.Count == 33)).IsTrue();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task Run_ListsSameNamedFilesWithDifferentAudio_AsSuspects()
    {
        var rootA = Directory.CreateTempSubdirectory("suspect-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("suspect-b").FullName;
        try
        {
            // Same name on both sides, different audio: neither file can pair
            // by ear, so the suspect pair is the only place the disagreement
            // shows. A third file with a name of its own is nobody's suspect.
            File.Copy(P("chirp.wav"), Path.Combine(rootA, "Shared Title.wav"));
            File.Copy(P("noise.wav"), Path.Combine(rootB, "Shared Title.wav"));
            File.Copy(P("tones-a.wav"), Path.Combine(rootA, "Lonely.wav"));

            var result = DuplicateScan.Run(Ff, [rootA, rootB], jobs: 2, minDurationSeconds: 0);

            await Assert.That(result.Suspects).HasSingleItem();
            var pair = result.Suspects[0];
            await Assert.That(pair.PathA).IsEqualTo(Path.Combine(rootA, "Shared Title.wav"));
            await Assert.That(pair.PathB).IsEqualTo(Path.Combine(rootB, "Shared Title.wav"));
            await Assert.That(pair.RootA).IsEqualTo(rootA);
            await Assert.That(pair.RootB).IsEqualTo(rootB);
            await Assert.That(pair.Strength).IsEqualTo(NameStrength.SameName);
            await Assert.That(pair.SameAudioAsA).IsNull();
            await Assert.That(pair.SameAudioAsB).IsNull();
            await IsAboutThreeSeconds(pair.SecondsA);
            await IsAboutThreeSeconds(pair.SecondsB);
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Test]
    public async Task Run_ASuspectCanBeGroupedWithAThirdFile_AndNamesIt()
    {
        var rootA = Directory.CreateTempSubdirectory("suspect-third-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("suspect-third-b").FullName;
        try
        {
            // B's MP3 is the same audio as A's all-artists copy (they group),
            // while A's plain-named file under the very same name is different
            // audio. The MP3 is still a suspect against that plain name, and
            // its side of the pair says where its audio really is.
            File.Copy(P("chirp.wav"), Path.Combine(rootA, "Artist Name - Song Title.wav"));
            File.Copy(P("tones-a.wav"), Path.Combine(rootA, "Artist Name; Guest Two - Song Title.wav"));
            File.Copy(P("tones-a-128.mp3"), Path.Combine(rootB, "Artist Name - Song Title.mp3"));

            var result = DuplicateScan.Run(Ff, [rootA, rootB], jobs: 2, minDurationSeconds: 0);

            var guestCopy = Path.Combine(rootA, "Artist Name; Guest Two - Song Title.wav");
            var mp3 = Path.Combine(rootB, "Artist Name - Song Title.mp3");
            await Assert.That(result.Groups).HasSingleItem();
            var members = result.Groups[0].Group.Members.Select(m => m.Path).ToList();
            await Assert.That(members.Count).IsEqualTo(2);
            await Assert.That(members).Contains(guestCopy);
            await Assert.That(members).Contains(mp3);

            await Assert.That(result.Suspects).HasSingleItem();
            var pair = result.Suspects[0];
            await Assert.That(pair.PathA).IsEqualTo(Path.Combine(rootA, "Artist Name - Song Title.wav"));
            await Assert.That(pair.PathB).IsEqualTo(mp3);
            await Assert.That(pair.SameAudioAsA).IsNull();
            await Assert.That(pair.SameAudioAsB).IsEqualTo(guestCopy);
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Test]
    public async Task Run_FilesTooShortToFingerprint_StillPair_WithTheirLengths()
    {
        var rootA = Directory.CreateTempSubdirectory("suspect-short-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("suspect-short-b").FullName;
        try
        {
            // Under the fingerprint minimum a file is never grouped, but it was
            // still decoded for its audit row, and a name twin under the other
            // folder may be a different recording all the same. The lengths
            // come from the rows Partition set aside.
            File.Copy(P("chirp.wav"), Path.Combine(rootA, "Shared Title.wav"));
            File.Copy(P("chirp.wav"), Path.Combine(rootB, "Shared Title.wav"));

            var result = DuplicateScan.Run(Ff, [rootA, rootB], jobs: 2, minDurationSeconds: 3600);

            await Assert.That(result.NotAnalyzed.Count).IsEqualTo(2);
            await Assert.That(result.Groups).IsEmpty();
            await Assert.That(result.Suspects).HasSingleItem();
            await IsAboutThreeSeconds(result.Suspects[0].SecondsA);
            await IsAboutThreeSeconds(result.Suspects[0].SecondsB);
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Test]
    public async Task Run_EveryUnpairedFileWithATwin_IsInASuspectPair()
    {
        var rootA = Directory.CreateTempSubdirectory("suspect-all-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("suspect-all-b").FullName;
        try
        {
            // The files of the first two suspect tests, in one scan. The folder
            // diff points every name-matched row at the suspects section with
            // no state of its own, which is honest only while no such file is
            // missing from it.
            File.Copy(P("chirp.wav"), Path.Combine(rootA, "Shared Title.wav"));
            File.Copy(P("noise.wav"), Path.Combine(rootB, "Shared Title.wav"));
            File.Copy(P("tones-a.wav"), Path.Combine(rootA, "Lonely.wav"));
            File.Copy(P("chirp.wav"), Path.Combine(rootA, "Artist Name - Song Title.wav"));
            File.Copy(P("tones-a.wav"), Path.Combine(rootA, "Artist Name; Guest Two - Song Title.wav"));
            File.Copy(P("tones-a-128.mp3"), Path.Combine(rootB, "Artist Name - Song Title.mp3"));

            var result = DuplicateScan.Run(Ff, [rootA, rootB], jobs: 2, minDurationSeconds: 0);

            var named = result.Unpaired.Where(u => u.TwinPath is not null).ToList();
            await Assert.That(named).IsNotEmpty(); // else the invariant holds over nothing
            var suspected = result.Suspects
                .SelectMany(s => new[] { s.PathA, s.PathB })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            await Assert.That(named.Where(u => !suspected.Contains(u.Path)).Select(u => u.Path)).IsEmpty();
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Test]
    public async Task Run_ARootSpelledThroughDotDot_StillPairsUnderItsFullPath()
    {
        var rootA = Directory.CreateTempSubdirectory("suspect-spelled-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("suspect-spelled-b").FullName;
        try
        {
            // The CLI hands roots over as typed, while the files a scan finds
            // carry the full path of their root. B is spelled through A's parent
            // here: unless the scan normalizes its roots, B's file matches no
            // root and its side of the pair carries "" where B belongs.
            var spelledB = Path.Combine(rootA, "..", Path.GetFileName(rootB));
            File.Copy(P("chirp.wav"), Path.Combine(rootA, "Shared Title.wav"));
            File.Copy(P("noise.wav"), Path.Combine(rootB, "Shared Title.wav"));

            var result = DuplicateScan.Run(Ff, [rootA, spelledB], jobs: 2, minDurationSeconds: 0);

            await Assert.That(result.Suspects).HasSingleItem();
            var pair = result.Suspects[0];
            await Assert.That(pair.PathA).IsEqualTo(Path.Combine(rootA, "Shared Title.wav"));
            await Assert.That(pair.RootB).IsEqualTo(rootB);
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    private static async Task IsAboutThreeSeconds(double? seconds)
    {
        await Assert.That(seconds).IsNotNull();
        await Assert.That(seconds!.Value).IsBetween(2.9, 3.1);
    }

    [Test]
    public async Task Run_ListsEveryCollectedFile_WithItsGroupLengthRowAndSize()
    {
        var rootA = Directory.CreateTempSubdirectory("listed-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("listed-b").FullName;
        try
        {
            // A grouped pair, a stranger and a file nothing can decode. Every
            // collected file is listed whatever became of it, so a check built
            // on the scan can still name the files it could not use.
            var (tonesWav, chirp, tonesMp3, broken) = CopyListedLibrary(rootA, rootB);

            var result = DuplicateScan.Run(Ff, [rootA, rootB], jobs: 2, minDurationSeconds: 0);

            string[] inPathOrder = [.. new[] { tonesWav, chirp, tonesMp3, broken }.Order(StringComparer.OrdinalIgnoreCase)];
            await Assert.That(result.Files.Select(f => f.Path).SequenceEqual(inPathOrder)).IsTrue();
            await Assert.That(result.FilesScanned).IsEqualTo(result.Files.Count);

            var listed = result.Files.ToDictionary(f => f.Path);
            foreach (var (path, root, analyzed, grouped) in new[]
            {
                (tonesWav, rootA, true, true),
                (chirp, rootA, true, false),
                (tonesMp3, rootB, true, true),
                (broken, rootB, false, false),
            })
            {
                var file = listed[path];
                await Assert.That(file.Root).IsEqualTo(Path.GetFullPath(root));
                await Assert.That(file.SizeBytes).IsEqualTo(new FileInfo(path).Length);
                await Assert.That(file.Row is not null).IsEqualTo(analyzed);
                await Assert.That(file.Seconds).IsEqualTo(file.Row?.DurationSeconds);
                await Assert.That(file.Group is not null).IsEqualTo(grouped);
            }
            await Assert.That(listed[tonesMp3].Group).IsEqualTo(listed[tonesWav].Group);
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Test]
    public async Task Run_ASingleFolder_StillListsItsFiles()
    {
        var root = Directory.CreateTempSubdirectory("listed-one").FullName;
        try
        {
            // One folder has no suspects to look for, and its file list must
            // not hang on that walk: the list is built whatever the roots.
            File.Copy(P("tones-a.wav"), Path.Combine(root, "tones-a.wav"));
            File.Copy(P("chirp.wav"), Path.Combine(root, "chirp.wav"));

            var result = DuplicateScan.Run(Ff, [root], jobs: 2, minDurationSeconds: 0);

            await Assert.That(result.Files.Count).IsEqualTo(2);
            await Assert.That(result.Suspects).IsEmpty();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task Run_Similarity_GivesAMatchedPairItsScore_AndStrangersZero()
    {
        var rootA = Directory.CreateTempSubdirectory("similar-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("similar-b").FullName;
        try
        {
            var (tonesWav, chirp, tonesMp3, _) = CopyListedLibrary(rootA, rootB);

            var result = DuplicateScan.Run(Ff, [rootA, rootB], jobs: 2, minDurationSeconds: 0);

            // The matcher's own score: a matched pair cleared the bar it was
            // matched at, and the pair has one score whichever path comes first.
            var score = result.Similarity(tonesWav, tonesMp3);
            await Assert.That(score).IsGreaterThanOrEqualTo(FingerprintMatcher.MidThreshold);
            await Assert.That(result.Similarity(tonesMp3, tonesWav)).IsEqualTo(score);
            await Assert.That(result.Similarity(tonesWav, chirp)).IsEqualTo(0.0);
            await Assert.That(result.Similarity(tonesWav, Path.Combine(rootA, "never scanned.wav"))).IsEqualTo(0.0);
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    [Test]
    public async Task Run_ListsEachFilesModifiedTime()
    {
        var rootA = Directory.CreateTempSubdirectory("mtime-a").FullName;
        var rootB = Directory.CreateTempSubdirectory("mtime-b").FullName;
        try
        {
            var (tonesWav, chirp, tonesMp3, broken) = CopyListedLibrary(rootA, rootB);

            // A time of its own for each file, so one read from another file cannot pass.
            string[] paths = [tonesWav, chirp, tonesMp3, broken];
            for (var i = 0; i < paths.Length; i++)
                File.SetLastWriteTimeUtc(paths[i], new DateTime(2021, 3, 4, 5, 6, 7, DateTimeKind.Utc).AddMinutes(i * 37 + 1));

            var result = DuplicateScan.Run(Ff, [rootA, rootB], jobs: 2, minDurationSeconds: 0);

            await Assert.That(result.Files.Count).IsEqualTo(4);
            await Assert.That(result.Files.Select(f => f.MtimeTicks).Distinct().Count()).IsEqualTo(4);
            foreach (var file in result.Files)
                await Assert.That(file.MtimeTicks).IsEqualTo(File.GetLastWriteTimeUtc(file.Path).Ticks);
        }
        finally
        {
            Directory.Delete(rootA, recursive: true);
            Directory.Delete(rootB, recursive: true);
        }
    }

    /// The library the listing and similarity tests share: one recording in
    /// two formats under unrelated names, a stranger, and a file no ffmpeg
    /// build decodes. corrupt.flac will not do for that last one: some builds
    /// decode it in part, and then it has a row.
    private static (string TonesWav, string Chirp, string TonesMp3, string Broken) CopyListedLibrary(
        string rootA, string rootB)
    {
        var tonesWav = Path.Combine(rootA, "Tones Song.wav");
        var chirp = Path.Combine(rootA, "chirp.wav");
        var tonesMp3 = Path.Combine(rootB, "completely unrelated name.mp3");
        var broken = Path.Combine(rootB, "broken.flac");
        File.Copy(P("tones-a.wav"), tonesWav);
        File.Copy(P("chirp.wav"), chirp);
        File.Copy(P("tones-a-128.mp3"), tonesMp3);
        File.Copy(P("notaudio.txt"), broken);
        return (tonesWav, chirp, tonesMp3, broken);
    }
}
