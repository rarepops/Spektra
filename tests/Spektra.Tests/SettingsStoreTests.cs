using Spektra.Core;

namespace Spektra.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "spektra-tests", Guid.NewGuid().ToString("N"));

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string SettingsPath => Path.Combine(_dir, "sub", "settings.json");

    [Test]
    public async Task Load_MissingFile_ReturnsDefaults()
    {
        var s = SettingsStore.Load(SettingsPath);
        await Assert.That(s.Window).IsNull();
        await Assert.That(s.RecentFiles).IsEmpty();
    }

    [Test]
    public async Task SaveLoad_RoundTrips()
    {
        var s = new AppSettings { Window = new WindowPlacement(10, 20, 800, 600, true) };
        s.PushRecent(@"C:\music\a.flac");
        SettingsStore.Save(SettingsPath, s);

        var r = SettingsStore.Load(SettingsPath);
        await Assert.That(r.Window).IsEqualTo(new WindowPlacement(10, 20, 800, 600, true));
        await Assert.That(r.RecentFiles.SequenceEqual([@"C:\music\a.flac"])).IsTrue();
    }

    [Test]
    public async Task FolderLayout_DefaultsNull_AndRoundTrips()
    {
        var defaults = SettingsStore.Load(SettingsPath);
        await Assert.That(defaults.FolderColumnWidths).IsNull();
        await Assert.That(defaults.FolderTreeWidth).IsNull();

        var s = new AppSettings
        {
            FolderColumnWidths = new() { ["File"] = 340.5, ["Integrity"] = 90 },
            FolderTreeWidth = 275,
        };
        SettingsStore.Save(SettingsPath, s);

        var r = SettingsStore.Load(SettingsPath);
        await Assert.That(r.FolderTreeWidth).IsEqualTo(275);
        await Assert.That(r.FolderColumnWidths!["File"]).IsEqualTo(340.5);
        await Assert.That(r.FolderColumnWidths["Integrity"]).IsEqualTo(90);
    }

    [Test]
    public async Task DuplicateDetectiveState_DefaultsNull_AndRoundTrips()
    {
        var defaults = SettingsStore.Load(SettingsPath);
        await Assert.That(defaults.DuplicateRoots).IsNull();
        await Assert.That(defaults.DuplicatesWindow).IsNull();

        var s = new AppSettings
        {
            DuplicateRoots = [@"C:\Music", @"E:\More"],
            DuplicatesWindow = new WindowPlacement(10, 20, 900, 600, false),
        };
        SettingsStore.Save(SettingsPath, s);

        var r = SettingsStore.Load(SettingsPath);
        await Assert.That(r.DuplicateRoots!.SequenceEqual([@"C:\Music", @"E:\More"])).IsTrue();
        await Assert.That(r.DuplicatesWindow).IsEqualTo(new WindowPlacement(10, 20, 900, 600, false));
    }

    [Test]
    public async Task FolderManifestState_DefaultsNull_AndRoundTrips()
    {
        var defaults = SettingsStore.Load(SettingsPath);
        await Assert.That(defaults.FolderManifestFolder).IsNull();
        await Assert.That(defaults.FolderManifestWindow).IsNull();

        var s = new AppSettings
        {
            FolderManifestFolder = @"D:\Music",
            FolderManifestWindow = new WindowPlacement(20, 40, 900, 600, false),
        };
        SettingsStore.Save(SettingsPath, s);

        var r = SettingsStore.Load(SettingsPath);
        await Assert.That(r.FolderManifestFolder).IsEqualTo(@"D:\Music");
        await Assert.That(r.FolderManifestWindow).IsEqualTo(new WindowPlacement(20, 40, 900, 600, false));
    }

    [Test]
    public async Task ManifestColumnWidths_DefaultNull_AndRoundTrips()
    {
        var defaults = SettingsStore.Load(SettingsPath);
        await Assert.That(defaults.ManifestColumnWidths).IsNull();

        var s = new AppSettings { ManifestColumnWidths = new() { ["Kind"] = 90, ["Size"] = 120.5 } };
        SettingsStore.Save(SettingsPath, s);

        var r = SettingsStore.Load(SettingsPath);
        await Assert.That(r.ManifestColumnWidths!["Kind"]).IsEqualTo(90);
        await Assert.That(r.ManifestColumnWidths["Size"]).IsEqualTo(120.5);
    }

    [Test]
    public async Task SavedColumnWidth_HonorsOnlySaneValues()
    {
        // Never saved (no map at all, or no entry for this header) falls back
        // to the caller's default, so new columns are unaffected by old files.
        await Assert.That(AppSettings.SavedColumnWidth(null, "Kind", 64, 40, 600)).IsEqualTo(64);
        await Assert.That(AppSettings.SavedColumnWidth(
            new() { ["Size"] = 90 }, "Kind", 64, 40, 600)).IsEqualTo(64);

        // Saved and sane is honored; hand-edited junk clamps or defaults.
        var w = new Dictionary<string, double>
        {
            ["Kind"] = 90,
            ["Tiny"] = 3,
            ["Huge"] = 5000,
            ["Junk"] = double.NaN,
        };
        await Assert.That(AppSettings.SavedColumnWidth(w, "Kind", 64, 40, 600)).IsEqualTo(90);
        await Assert.That(AppSettings.SavedColumnWidth(w, "Tiny", 64, 40, 600)).IsEqualTo(40);
        await Assert.That(AppSettings.SavedColumnWidth(w, "Huge", 64, 40, 600)).IsEqualTo(600);
        await Assert.That(AppSettings.SavedColumnWidth(w, "Junk", 64, 40, 600)).IsEqualTo(64);
    }

    [Test]
    public async Task Session_DefaultsOff_AndRoundTrips()
    {
        // An old settings file has none of these keys: restore must default
        // to off so upgrading never reopens tabs the user did not ask for.
        var defaults = SettingsStore.Load(SettingsPath);
        await Assert.That(defaults.RestoreSession).IsFalse();
        await Assert.That(defaults.SessionTabs).IsNull();
        await Assert.That(defaults.SessionSelectedIndex).IsEqualTo(0);

        var s = new AppSettings
        {
            RestoreSession = true,
            SessionTabs = [@"C:\music\a.flac", @"C:\music\Album"],
            SessionSelectedIndex = 1,
        };
        SettingsStore.Save(SettingsPath, s);

        var r = SettingsStore.Load(SettingsPath);
        await Assert.That(r.RestoreSession).IsTrue();
        await Assert.That(r.SessionTabs!.SequenceEqual(
            [@"C:\music\a.flac", @"C:\music\Album"])).IsTrue();
        await Assert.That(r.SessionSelectedIndex).IsEqualTo(1);
    }

    [Test]
    public async Task AutoIntegrityCheck_DefaultsTrue_AndRoundTripsOff()
    {
        // Absent from old settings files (and missing files) it must be ON.
        var defaults = SettingsStore.Load(SettingsPath);
        await Assert.That(defaults.AutoIntegrityCheck).IsTrue();

        SettingsStore.Save(SettingsPath, new AppSettings { AutoIntegrityCheck = false });
        await Assert.That(SettingsStore.Load(SettingsPath).AutoIntegrityCheck).IsFalse();
    }

    [Test]
    public async Task ShowCrosshair_DefaultsOn_AndRoundTrips()
    {
        // Built-ins only: keep the test independent of the user's palette folder.
        var palettes = PaletteRegistry.LoadWithCustom(Path.Combine(Path.GetTempPath(), "spektra-no-palettes"));

        await Assert.That(SettingsStore.Load(SettingsPath).ShowCrosshair).IsTrue();
        await Assert.That(new AppSettings().ToDisplaySettings(palettes).ShowCrosshair).IsTrue();

        var s = new AppSettings { ShowCrosshair = false };
        SettingsStore.Save(SettingsPath, s);
        var r = SettingsStore.Load(SettingsPath);
        await Assert.That(r.ShowCrosshair).IsFalse();
        await Assert.That(r.ToDisplaySettings(palettes).ShowCrosshair).IsFalse();
    }

    [Test]
    public async Task Load_CorruptJson_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ this is not json");
        var s = SettingsStore.Load(SettingsPath);
        await Assert.That(s.Window).IsNull();
    }

    [Test]
    public async Task Save_Succeeds_ReturnsTrue()
    {
        await Assert.That(SettingsStore.Save(SettingsPath, new AppSettings())).IsTrue();
    }

    [Test]
    public async Task Save_UnwritableLocation_FailsSoft_ReturnsFalse()
    {
        // The parent is a file, so the settings directory can't be created:
        // Save must fail soft (mirroring Load) rather than throw, so a
        // read-only AppData never crashes the app.
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "x");
        var target = Path.Combine(blocker, "settings.json");
        await Assert.That(SettingsStore.Save(target, new AppSettings())).IsFalse();
    }

    [Test]
    public async Task PushRecent_DedupesCaseInsensitive_MovesToFront()
    {
        var s = new AppSettings();
        s.PushRecent(@"C:\a.mp3");
        s.PushRecent(@"C:\b.mp3");
        s.PushRecent(@"C:\A.MP3");
        await Assert.That(s.RecentFiles.SequenceEqual([@"C:\A.MP3", @"C:\b.mp3"])).IsTrue();
    }

    [Test]
    public async Task PushRecent_CapsAtMaxRecent()
    {
        var s = new AppSettings();
        for (var i = 1; i <= 15; i++) s.PushRecent($@"C:\{i}.mp3");
        await Assert.That(s.RecentFiles.Count).IsEqualTo(AppSettings.MaxRecent);
        await Assert.That(s.RecentFiles[0]).IsEqualTo(@"C:\15.mp3");
    }
}
