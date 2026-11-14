using System.Diagnostics;
using Spektra.App;
using Spektra.Core;

namespace Spektra.Tests;

/// Drives Duplicate Detective's folder diff through its view model, over two
/// folders built once for the class. FolderDiffViewTests pins the rules; this
/// pins the wiring around them: which roots, flags and filter words reach
/// those rules, which numbers fill the footer, when the columns are built and
/// what each row says.
///
/// Never read a brush here (DupeMemberItem.IntegrityBrush or any other). An
/// Avalonia brush belongs to the thread that created it, NodeMarkers makes its
/// brushes on whichever thread first builds a group, and reading one from
/// another thread throws. Passing them around, as the view model does, is fine.
public sealed class DuplicatesViewModelTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static string P(string file) => Path.Combine(Fixtures, file);

    private static string _library = "";
    private static string RootA => Path.Combine(_library, "A");
    private static string RootB => Path.Combine(_library, "B");

    /// The two folders in the order the window makes columns of them, each with
    /// the one file that only it holds.
    private static (string Root, string OnlyHere)[] Sides =>
        [(RootA, "only in a.wav"), (RootB, "only in b.wav")];

    [Before(Class)]
    public static void BuildLibrary()
    {
        // Hex digits can never spell the filter words below (only, tones, zzz),
        // and the filter reads whole paths; a random temp name could spell one.
        _library = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "spektra-dupes-vm-" + Guid.NewGuid().ToString("N"))).FullName;
        var a = Directory.CreateDirectory(RootA).FullName;
        var b = Directory.CreateDirectory(RootB).FullName;

        // One recording in two formats and under two names (the group a diff
        // hides), one stranger in each folder, one name both folders share over
        // different audio, and the two files a scan cannot compare: one it
        // cannot decode and one shorter than the minimum. A text file shows the
        // scan ignores what is not audio.
        Loop("tones-a.wav", Path.Combine(a, "Tones Song.wav"));
        Loop("noise.wav", Path.Combine(a, "only in a.wav"));
        Loop("sine-1khz.wav", Path.Combine(a, "Shared Title.wav"));
        File.Copy(P("corrupt.flac"), Path.Combine(a, "corrupt.flac"));
        File.Copy(P("notaudio.txt"), Path.Combine(a, "notaudio.txt"));

        Loop("tones-b.wav", Path.Combine(b, "Shared Title.wav"));
        Loop("chirp.wav", Path.Combine(b, "only in b.wav"));
        // Made from the WAV above, so it has to come after it.
        Ffmpeg("-i", Path.Combine(a, "Tones Song.wav"), "-codec:a", "libmp3lame", "-b:a", "128k",
            Path.Combine(b, "completely unrelated name.mp3"));
        File.Copy(P("sine-1khz.mp3"), Path.Combine(b, "sine-1khz.mp3"));
    }

    [After(Class)]
    public static void DeleteLibrary() => Directory.Delete(_library, recursive: true);

    /// Every fixture is 3 s long and the view model always scans with Duplicate
    /// Detective's 20 s minimum (the Core tests lower it, the view model cannot),
    /// so each song is its fixture looped eight times, 24 s, without re-encoding.
    private static void Loop(string fixture, string target) =>
        Ffmpeg("-stream_loop", "7", "-i", P(fixture), "-c", "copy", target);

    private static void Ffmpeg(params string[] args)
    {
        var psi = new ProcessStartInfo(Ff.FfmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in (string[])["-nostdin", "-v", "error", "-y", .. args])
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed ({p.ExitCode}): {stderr}");
    }

    /// Read off the file the hook built, never typed in: WAV headers and the
    /// MP3 encode differ between ffmpeg builds.
    private static string Bytes(string folder, string name) =>
        Reporting.FormatBytes(new FileInfo(Path.Combine(folder, name)).Length);

    private static string CachePath(string work) => Path.Combine(work, "audit-cache.db");

    /// The size of the copy a clean-up would drop: the MP3.
    private static string Reclaimable() => Bytes(RootB, "completely unrelated name.mp3");

    /// The first half of the footer, the same in every view: groups found, the
    /// files in them, what dropping the extra copies would reclaim (the MP3),
    /// and files scanned.
    private static string ScanLine() =>
        $"1 groups · 2 duplicate files · reclaimable {Reclaimable()} · 8 scanned";

    /// The one group's headline, as the ordinary view lists it.
    private static string Headline() =>
        $"tones song · 2 files · sameness High · reclaim {Reclaimable()}";

    /// The --diff launch: both roots, the diff already on, then the scan. The
    /// cache is a file in the test's own folder, never the user's.
    private static async Task<DuplicatesViewModel> ScanTheLibrary(string work)
    {
        var vm = new DuplicatesViewModel(Ff, new AppSettings(), cachePath: CachePath(work));
        vm.SetDiffRoots(RootA, RootB);
        await vm.ScanAsync();
        return vm;
    }

    [Test]
    public async Task ScanAsync_InADiff_HidesTheSameRecording_AndFillsEachColumnWithWhatOnlyItHolds()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            var vm = await ScanTheLibrary(work);

            // The one duplicate pair is the same recording, and a diff hides those.
            await Assert.That(vm.Groups).IsEmpty();

            await Assert.That(vm.DiffColumns.Count).IsEqualTo(2);
            for (var i = 0; i < 2; i++)
            {
                var (root, onlyHere) = Sides[i];
                var column = vm.DiffColumns[i];
                await Assert.That(column.Root).IsEqualTo(root);
                await Assert.That(column.Files).HasSingleItem();
                await Assert.That(column.Files[0].Relative).IsEqualTo(onlyHere);
                await Assert.That(column.Files[0].TwinNote).IsNull();
                await Assert.That(column.Summary).IsEqualTo(
                    $"1 file(s) only here · {Bytes(root, onlyHere)} · 1 the other folder has under a matching name");
            }

            await Assert.That(vm.FooterText).IsEqualTo(
                ScanLine() + " · differences: 1 same hidden · 2 same name only · 2 in one folder only"
                + " · 0 weak match · 2 not comparable");
            await Assert.That(vm.NotAnalyzed.Select(n => Path.GetFileName(n.Path)))
                .IsEquivalentTo(new[] { "corrupt.flac", "sine-1khz.mp3" });
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task HideTitleTwins_Off_ListsTheNameMatchesAfterTheFilesOnlyHere_AndLeavesTheFooterAlone()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            var vm = await ScanTheLibrary(work);

            vm.HideTitleTwins = false;

            await Assert.That(vm.DiffColumns.Count).IsEqualTo(2);
            for (var i = 0; i < 2; i++)
            {
                var onlyHere = Sides[i].OnlyHere;
                var files = vm.DiffColumns[i].Files;
                await Assert.That(string.Join("|", files.Select(f => f.Relative)))
                    .IsEqualTo($"{onlyHere}|Shared Title.wav");
                await Assert.That(files[0].TwinNote).IsNull();
                await Assert.That(files[1].TwinNote)
                    .IsEqualTo("the other folder has this name too · the audio differs");
            }

            // The footer counts name matches whether or not the columns list
            // them, so it reads as it did before the box was unticked.
            await Assert.That(vm.FooterText).IsEqualTo(
                ScanLine() + " · differences: 1 same hidden · 2 same name only · 2 in one folder only"
                + " · 0 weak match · 2 not comparable");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task FilterText_NarrowsTheColumns_AndTheFooterNamesWhatItKept()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            var vm = await ScanTheLibrary(work);

            // With the name matches showing, the filter has a visible row to take away.
            vm.HideTitleTwins = false;
            vm.FilterText = "only";

            await Assert.That(vm.DiffColumns.Count).IsEqualTo(2);
            for (var i = 0; i < 2; i++)
            {
                var (root, onlyHere) = Sides[i];
                var column = vm.DiffColumns[i];
                await Assert.That(column.Files).HasSingleItem();
                await Assert.That(column.Files[0].Relative).IsEqualTo(onlyHere);
                // The filter takes the name match out of the column, so the
                // clause that counts it leaves the summary too.
                await Assert.That(column.Summary).IsEqualTo($"1 file(s) only here · {Bytes(root, onlyHere)}");
            }

            await Assert.That(vm.FooterText).IsEqualTo(
                ScanLine() + " · differences: 1 same hidden · 2 in one folder only · 0 weak match"
                + " · 2 not comparable · filter: 0 of 1 groups");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task OnlyDifferences_Off_ShowsTheDuplicateGroup_AndEmptiesTheColumns()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            var vm = await ScanTheLibrary(work);

            // Each step builds on the one before, as in the click-through.
            vm.HideTitleTwins = false;
            vm.FilterText = "only";
            vm.FilterText = "";
            vm.OnlyDifferences = false;

            await Assert.That(vm.Groups).HasSingleItem();
            await Assert.That(vm.Groups[0].Headline).IsEqualTo(Headline());
            await Assert.That(vm.DiffColumns).IsEmpty();
            await Assert.That(vm.FooterText).IsEqualTo(ScanLine());
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task FilterText_OutsideADiff_KeepsTheMatchingGroup_AndDropsItWhenNothingMatches()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            var vm = await ScanTheLibrary(work);

            // In a diff this group is hidden whatever the words say; outside one they decide.
            vm.OnlyDifferences = false;
            vm.FilterText = "tones";

            await Assert.That(vm.Groups).HasSingleItem();
            await Assert.That(vm.Groups[0].Headline).IsEqualTo(Headline());
            await Assert.That(vm.FooterText).IsEqualTo(ScanLine() + " · filter: 1 of 1 groups");

            vm.FilterText = "zzz";

            await Assert.That(vm.Groups).IsEmpty();
            await Assert.That(vm.FooterText).IsEqualTo(ScanLine() + " · filter: 0 of 1 groups");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task ScanAsync_WritesTheCacheFileItWasGiven()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            var vm = await ScanTheLibrary(work);

            await Assert.That(File.Exists(CachePath(work))).IsTrue()
                .Because($"the scan ended with: {vm.FooterText}");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    /// The diff's footer exactly as ScanAsync_InADiff_HidesTheSameRecording_AndFillsEachColumnWithWhatOnlyItHolds
    /// expects it: the suspects section must not move it.
    private static string DiffFooter() =>
        ScanLine() + " · differences: 1 same hidden · 2 same name only · 2 in one folder only"
        + " · 0 weak match · 2 not comparable";

    /// The fixture's one suspect: the name both folders hold over different audio.
    private static string SharedTitle(string root) => Path.Combine(root, "Shared Title.wav");

    [Test]
    public async Task ScanAsync_InADiff_ListsTheSharedTitle_AsASuspect()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            var vm = await ScanTheLibrary(work);

            await Assert.That(vm.Suspects).HasSingleItem();
            var row = vm.Suspects[0];
            await Assert.That(row.Name).IsEqualTo("Shared Title");
            // A 1 kHz sine against a melody that holds no 1 kHz partial.
            await Assert.That(row.LabelText).IsEqualTo("Doesn't line up: a cover, another song, or a heavy remaster");
            await Assert.That(row.IsBottomTier).IsFalse();
            await Assert.That(row.Lengths).IsEqualTo("0:24 vs 0:24");
            await Assert.That(row.Strength).IsEqualTo("same name");
            await Assert.That(row.HasCrossMatch).IsFalse();
            // FullPath is what the row's context menu acts on.
            await Assert.That(((IFileItem)row.SideA).FullPath).IsEqualTo(SharedTitle(RootA));
            await Assert.That(row.SideA.Label).IsEqualTo("A");
            await Assert.That(((IFileItem)row.SideB).FullPath).IsEqualTo(SharedTitle(RootB));
            await Assert.That(row.SideB.Label).IsEqualTo("B");
            await Assert.That(vm.SuspectHeading)
                .IsEqualTo("Same name, different audio · 1 to listen to first · 0 similar");
            await Assert.That(vm.FooterText).IsEqualTo(DiffFooter());
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task HideTitleTwins_NeverHidesASuspect()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            var vm = await ScanTheLibrary(work);

            // On (the default), off, then on again: the box thins the columns, never this section.
            foreach (var hide in (bool[])[true, false, true])
            {
                vm.HideTitleTwins = hide;

                await Assert.That(vm.Suspects).HasSingleItem().Because($"Hide name matches is {(hide ? "on" : "off")}");
                await Assert.That(vm.SuspectHeading)
                    .IsEqualTo("Same name, different audio · 1 to listen to first · 0 similar")
                    .Because($"Hide name matches is {(hide ? "on" : "off")}");
            }
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task OnlyDifferences_Off_EmptiesTheSuspects()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            var vm = await ScanTheLibrary(work);

            vm.OnlyDifferences = false;

            await Assert.That(vm.Suspects).IsEmpty();
            // The scan found a pair, but none is on screen to save.
            await Assert.That(vm.CanSavePlaylist).IsFalse();
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task FilterText_NarrowsTheSuspects_AndTheHeadingCountsWhatIsShown()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            var vm = await ScanTheLibrary(work);

            vm.FilterText = "zzz";

            await Assert.That(vm.Suspects).IsEmpty();
            await Assert.That(vm.SuspectHeading).IsEqualTo("Same name, different audio · none");
            await Assert.That(vm.CanSavePlaylist).IsFalse();

            vm.FilterText = "shared";

            await Assert.That(vm.Suspects).HasSingleItem();
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task RequestCompare_OnASuspect_AsksForAnAlignedCompare_FirstFolderAsA()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            var vm = await ScanTheLibrary(work);
            await Assert.That(vm.Suspects).HasSingleItem();
            var aligned = new List<string>();
            var compares = 0;
            var opens = 0;
            vm.OpenAlignedCompareRequested += (a, b) => aligned.Add($"{a} then {b}");
            vm.OpenCompareRequested += (_, _) => compares++;
            vm.OpenFileRequested += _ => opens++;

            vm.RequestCompare(vm.Suspects[0]);

            await Assert.That(string.Join(" | ", aligned))
                .IsEqualTo($"{SharedTitle(RootA)} then {SharedTitle(RootB)}");
            // The winner-against-challenger compare does not align, and nothing opens on its own.
            await Assert.That(compares).IsEqualTo(0);
            await Assert.That(opens).IsEqualTo(0);
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task ListeningPlaylist_HoldsThePair_AThenB()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            var vm = await ScanTheLibrary(work);

            await Assert.That(vm.ListeningPlaylist()).IsEqualTo(
                "#EXTM3U\n"
                + $"#EXTINF:24,1 A · doesn't line up · Shared Title\n{SharedTitle(RootA)}\n"
                + $"#EXTINF:24,1 B · doesn't line up · Shared Title\n{SharedTitle(RootB)}\n");
            await Assert.That(vm.CanSavePlaylist).IsTrue();
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task Cancel_BeforeTheLabelsArrive_LeavesThemNotChecked_AndKeepsTheFooter()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            // Built as ScanTheLibrary builds it, plus a Cancel the moment the results land:
            // HasResults turns true before the labelling starts, so no label ever arrives.
            var vm = new DuplicatesViewModel(Ff, new AppSettings(), cachePath: CachePath(work));
            vm.SetDiffRoots(RootA, RootB);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DuplicatesViewModel.HasResults) && vm.HasResults) vm.Cancel();
            };

            await vm.ScanAsync();

            await Assert.That(vm.Suspects).HasSingleItem();
            await Assert.That(vm.Suspects[0].LabelText).IsEqualTo("not checked");
            await Assert.That(vm.SuspectHeading)
                .IsEqualTo("Same name, different audio · 0 to listen to first · 0 similar · 1 not checked");
            // The scan itself finished, so its footer stands, not "Scan cancelled".
            await Assert.That(vm.FooterText).IsEqualTo(DiffFooter());
            await Assert.That(vm.IsScanning).IsFalse();
            await Assert.That(vm.CanSavePlaylist).IsFalse();
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task HasScanResults_HoldsFromTheResultsLanding_UntilTheyAreCleared()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            // Before any scan the section would say "none" before it knows.
            var fresh = new DuplicatesViewModel(Ff, new AppSettings(), cachePath: CachePath(work));
            await Assert.That(fresh.HasScanResults).IsFalse();

            var vm = await ScanTheLibrary(work);
            await Assert.That(vm.HasScanResults).IsTrue();

            // What the Clear all button calls.
            vm.ClearRoots();
            await Assert.That(vm.HasScanResults).IsFalse();
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task ScanAsync_CheckingTheLabels_KeepsTheColumnsItListed()
    {
        var work = Directory.CreateTempSubdirectory("dupes-vm").FullName;
        try
        {
            // Built as ScanTheLibrary builds it, plus a copy of the columns as first listed: the
            // first SuspectHeading raised while columns are on show comes from the first
            // ApplyGroupFilter, before the labels are checked.
            var vm = new DuplicatesViewModel(Ff, new AppSettings(), cachePath: CachePath(work));
            vm.SetDiffRoots(RootA, RootB);
            DiffColumnItem[]? listed = null;
            vm.PropertyChanged += (_, e) =>
            {
                if (listed is null && e.PropertyName == nameof(DuplicatesViewModel.SuspectHeading)
                    && vm.DiffColumns.Count > 0)
                    listed = [.. vm.DiffColumns];
            };

            await vm.ScanAsync();

            // Columns built anew would throw a list the user is scrolling back to the top.
            await Assert.That(vm.DiffColumns.Count).IsEqualTo(2);
            for (var i = 0; i < 2; i++)
                await Assert.That(ReferenceEquals(vm.DiffColumns[i], listed![i])).IsTrue().Because($"column {i + 1}");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Test]
    public async Task SuspectPanelHeight_DefaultsTo260_ClampsItsRange_AndSavesEveryChange()
    {
        // Never dragged: the list's old fixed ceiling. Hand-edited junk is clamped.
        await Assert.That(new DuplicatesViewModel(Ff, new AppSettings()).SuspectPanelHeight).IsEqualTo(260);
        await Assert.That(new DuplicatesViewModel(Ff, new AppSettings { DuplicatesSuspectPanelHeight = 9999 })
            .SuspectPanelHeight).IsEqualTo(2000);

        var settings = new AppSettings();
        var vm = new DuplicatesViewModel(Ff, settings);
        vm.SuspectPanelHeight = 40;
        await Assert.That(vm.SuspectPanelHeight).IsEqualTo(60);
        await Assert.That(settings.DuplicatesSuspectPanelHeight).IsEqualTo(60);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.SuspectPanelHeight = 300;
        await Assert.That(raised).Contains(nameof(DuplicatesViewModel.SuspectPanelHeight));
        await Assert.That(settings.DuplicatesSuspectPanelHeight).IsEqualTo(300);
    }
}
