using Spektra.App;
using Spektra.Core;
using static Spektra.Tests.TestAudio;

namespace Spektra.Tests;

/// Drives the Imposter Buster window's view model over ImposterLibrary, built once for the class.
/// ImposterScanTests pins the engine and ImposterViewTests the words and the order; this pins the
/// wiring around them: which rows each view lists, what the labels count, which folders the guards
/// refuse and which edits are saved.
///
/// The filter reads whole paths, and the temp folder's name is random hex after a fixed prefix, so
/// it can never spell the filter words below (three, zzz).
public sealed class ImposterViewModelTests
{
    private static ImposterLibrary _lib = null!;

    /// The class's own temp folder for audit caches, never the user's.
    private static string _work = "";

    /// The cache the scanning view models share. One scan fills it before the tests run, so each
    /// test's scan reads the analysis back instead of decoding every file again.
    private static string SharedCache => Path.Combine(_work, "audit-cache.db");

    [Before(Class)]
    public static void BuildLibrary()
    {
        _lib = new ImposterLibrary("spektra-imposter-vm-");
        _work = Directory.CreateTempSubdirectory("spektra-imposter-vm-").FullName;
        using var cache = AuditCache.Open(SharedCache);
        ImposterScan.Run(Ff, _lib.Rips, [_lib.Old, _lib.New], jobs: 2, cache);
    }

    [After(Class)]
    public static void DeleteLibrary()
    {
        _lib.Dispose();
        Directory.Delete(_work, recursive: true);
    }

    // A path as these tests name a file: its folder, then its name.
    private static string Short(string path) => $"{Path.GetFileName(Path.GetDirectoryName(path))}/{Path.GetFileName(path)}";

    // The rows as "kind folder/name", in the order listed.
    private static string Listed(IEnumerable<ImposterRowItem> rows) =>
        string.Join(", ", rows.Select(r => $"{r.Kind} {Short(r.Path)}"));

    private static ImposterRowItem RowOf(ImposterViewModel vm, string folder, string name) =>
        vm.Rows.Single(r => r.Path == Path.Combine(folder, name));

    private static string Labels(ImposterViewModel vm) =>
        $"{vm.ToHearLabel} | {vm.AllClearLabel} | {vm.NoReferenceLabel} | {vm.ReferenceOnlyLabel}";

    /// The launch switch's folders, then the scan, as the window runs them.
    private static async Task<ImposterViewModel> ScanTheLibrary()
    {
        var vm = new ImposterViewModel(Ff, new AppSettings(), cachePath: SharedCache);
        vm.SetFolders(_lib.Rips, [_lib.Old, _lib.New]);
        await vm.ScanAsync();
        return vm;
    }

    [Test]
    public async Task Scan_ListsTheSongsToHear_ReferenceRowThenCopies()
    {
        var vm = await ScanTheLibrary();

        // ImposterView's order with the all-clear song left out: the songs with no copy confirmed,
        // the copy that cannot be compared first, then the song where a copy differs.
        await Assert.That(Listed(vm.Rows)).IsEqualTo(
            "reference Rips/Artist Name - Song Four.wav, copy New/Artist Name - Song Four.wav, "
            + "reference Rips/Artist Name - Song Three.wav, copy Old/Artist Name - Song Three.wav, "
            + "reference Rips/Artist Name - Song One.mp3, copy Old/Artist Name - Song One.wav, "
            + "copy New/Artist Name - Song One.wav");

        var reference = vm.Rows[0];
        await Assert.That(reference.Grade).IsEqualTo("reference");
        await Assert.That(reference.CanCompare).IsFalse();
        // Old's copy shares its audio with the reference file; New's is another sound under the name.
        await Assert.That(RowOf(vm, _lib.Old, "Artist Name - Song One.wav").IsConfirmed).IsTrue();
        await Assert.That(RowOf(vm, _lib.New, "Artist Name - Song One.wav").IsConfirmed).IsFalse();
        await Assert.That(vm.FooterText).IsEqualTo(ImposterView.Summary(vm.LastResult!));
    }

    [Test]
    public async Task TheLabelsCountTheWholeResult()
    {
        var vm = await ScanTheLibrary();
        const string Counted = "To hear · 3 | All clear · 1 | No reference · 1 | Not in the checked folders · 1";

        await Assert.That(Labels(vm)).IsEqualTo(Counted);

        // Only Song Three's files hold the word, so the filter hides rows in every view.
        vm.FilterText = "three";

        await Assert.That(vm.Rows.Count).IsEqualTo(2);
        await Assert.That(Labels(vm)).IsEqualTo(Counted);
    }

    [Test]
    public async Task EachViewShowsItsOwnRows()
    {
        var vm = await ScanTheLibrary();

        vm.ShowAllClear = true;
        await Assert.That(Listed(vm.Rows)).IsEqualTo(
            "reference Rips/Artist Name - Song Two.wav, copy Old/Artist Name - Song Two.wav");

        vm.ShowNoReference = true;
        await Assert.That(Listed(vm.Rows)).IsEqualTo("no-reference Old/Bonus Track.wav");
        // A file no reference file names belongs to no song.
        await Assert.That(vm.Rows[0].Song).IsEqualTo("");

        vm.ShowReferenceOnly = true;
        await Assert.That(Listed(vm.Rows)).IsEqualTo("reference-only Rips/Rips Only.wav");
    }

    [Test]
    public async Task TheFilterNarrowsBySongOrPath()
    {
        var vm = await ScanTheLibrary();

        vm.FilterText = "three";
        await Assert.That(Listed(vm.Rows)).IsEqualTo(
            "reference Rips/Artist Name - Song Three.wav, copy Old/Artist Name - Song Three.wav");

        vm.FilterText = "zzz";
        await Assert.That(vm.Rows).IsEmpty();
        // The playlist holds the songs the filter leaves, so with none left there is nothing to save.
        await Assert.That(vm.CanSavePlaylist).IsFalse();
    }

    [Test]
    public async Task RequestCompare_ACopyRaisesItsReferenceThenItself_AReferenceRowRaisesNothing()
    {
        var vm = await ScanTheLibrary();
        var compares = new List<string>();
        var opens = 0;
        vm.OpenAlignedCompareRequested += (reference, copy) => compares.Add($"{reference} then {copy}");
        vm.OpenFileRequested += _ => opens++;

        vm.RequestCompare(RowOf(vm, _lib.New, "Artist Name - Song One.wav"));
        vm.RequestCompare(RowOf(vm, _lib.Rips, "Artist Name - Song One.mp3"));

        await Assert.That(string.Join(" | ", compares)).IsEqualTo(
            $"{Path.Combine(_lib.Rips, "Artist Name - Song One.mp3")} then {Path.Combine(_lib.New, "Artist Name - Song One.wav")}");
        // Nothing opens on its own either.
        await Assert.That(opens).IsEqualTo(0);
    }

    [Test]
    public async Task ListeningPlaylist_HoldsTheUnconfirmedCopiesAndTheirReferenceFiles()
    {
        var vm = await ScanTheLibrary();

        var playlist = vm.ListeningPlaylist();

        await Assert.That(playlist).Contains(Path.Combine(_lib.New, "Artist Name - Song One.wav"));
        await Assert.That(playlist).Contains(Path.Combine(_lib.Rips, "Artist Name - Song One.mp3"));
        await Assert.That(playlist).DoesNotContain("Song Two");
        await Assert.That(vm.CanSavePlaylist).IsTrue();
    }

    [Test]
    public async Task TheReferenceCannotBeAFolderToCheck()
    {
        const string Refused = "The reference folder cannot also be a folder to check.";

        // Old to check against the reference Rips, as the window would hold them.
        (ImposterViewModel Vm, AppSettings Settings) Holding()
        {
            var settings = new AppSettings();
            var vm = new ImposterViewModel(Ff, settings, cachePath: SharedCache);
            vm.AddRoot(_lib.Old);
            vm.TrySetReference(_lib.Rips);
            return (vm, settings);
        }

        // Each folder is spelled with a trailing separator, which only a comparison of full paths
        // sees through. The reference as a folder to check:
        var (vm, settings) = Holding();
        await Assert.That(vm.TryAddTypedRoot(_lib.Rips + Path.DirectorySeparatorChar)).IsFalse();
        await Assert.That(vm.FooterText).IsEqualTo(Refused);
        await Assert.That(string.Join(" | ", vm.Roots)).IsEqualTo(_lib.Old);
        await Assert.That(string.Join(" | ", settings.ImposterRoots ?? [])).IsEqualTo(_lib.Old);

        // A folder to check as the reference:
        (vm, settings) = Holding();
        await Assert.That(vm.TrySetReference(_lib.Old + Path.DirectorySeparatorChar)).IsFalse();
        await Assert.That(vm.FooterText).IsEqualTo(Refused);
        await Assert.That(vm.Reference).IsEqualTo(_lib.Rips);
        await Assert.That(settings.ImposterReference).IsEqualTo(_lib.Rips);
    }

    [Test]
    public async Task FoldersPersist_ExceptFromTheLaunchSwitch()
    {
        var settings = new AppSettings();
        var vm = new ImposterViewModel(Ff, settings, cachePath: SharedCache);
        vm.TrySetReference(_lib.Rips);
        vm.AddRoot(_lib.Old);

        await Assert.That(settings.ImposterReference).IsEqualTo(_lib.Rips);
        await Assert.That(string.Join(" | ", settings.ImposterRoots ?? [])).IsEqualTo(_lib.Old);

        // The launch switch shows its folders and leaves the saved ones as they were.
        var untouched = new AppSettings();
        var launched = new ImposterViewModel(Ff, untouched, cachePath: SharedCache);
        launched.SetFolders(_lib.Rips, [_lib.Old, _lib.New]);

        await Assert.That(launched.Reference).IsEqualTo(_lib.Rips);
        await Assert.That(string.Join(" | ", launched.Roots)).IsEqualTo($"{_lib.Old} | {_lib.New}");
        await Assert.That(untouched.ImposterReference).IsNull();
        await Assert.That(untouched.ImposterRoots).IsNull();
    }

    [Test]
    public async Task ClearRoots_DropsTheResultsAndKeepsTheReference()
    {
        var vm = await ScanTheLibrary();
        await Assert.That(vm.Rows).IsNotEmpty();

        vm.ClearRoots();

        await Assert.That(vm.Roots).IsEmpty();
        await Assert.That(vm.Rows).IsEmpty();
        await Assert.That(vm.LastResult).IsNull();
        await Assert.That(vm.FooterText).IsEqualTo("Choose a reference folder and one or more folders to check, then Scan.");
        await Assert.That(vm.Reference).IsEqualTo(_lib.Rips);
    }

    [Test]
    public async Task Scan_Cancelled_SaysSoAndShowsNoRows()
    {
        // Its own empty cache, so the scan has every file to decode and is still running when the
        // Cancel on the next line arrives.
        var ownCache = Path.Combine(_work, "cancelled.db");
        var vm = new ImposterViewModel(Ff, new AppSettings(), cachePath: ownCache);
        vm.SetFolders(_lib.Rips, [_lib.Old, _lib.New]);

        var scan = vm.ScanAsync();
        vm.Cancel();
        await scan;

        await Assert.That(vm.IsScanning).IsFalse();
        await Assert.That(vm.Rows).IsEmpty();
        await Assert.That(vm.LastResult).IsNull();
        await Assert.That(vm.FooterText).IsEqualTo("Scan cancelled · completed analysis stays cached");
        // The scan opened the cache it was given, never the user's.
        await Assert.That(File.Exists(ownCache)).IsTrue();
    }
}
