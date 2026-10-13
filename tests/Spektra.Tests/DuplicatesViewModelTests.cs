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
        _library = Directory.CreateTempSubdirectory("dupes-vm-library").FullName;
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

    /// The first half of the footer, the same in every view: groups found, the
    /// files in them, what dropping the extra copies would reclaim (the MP3),
    /// and files scanned.
    private static string ScanLine() =>
        $"1 groups · 2 duplicate files · reclaimable {Bytes(RootB, "completely unrelated name.mp3")} · 8 scanned";

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

            vm.FilterText = "";
            vm.OnlyDifferences = false;

            await Assert.That(vm.Groups).HasSingleItem();
            await Assert.That(vm.Groups[0].Headline).IsEqualTo(
                $"tones song · 2 files · sameness High · reclaim {Bytes(RootB, "completely unrelated name.mp3")}");
            await Assert.That(vm.DiffColumns).IsEmpty();
            await Assert.That(vm.FooterText).IsEqualTo(ScanLine());
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
}
