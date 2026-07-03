using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "spektra-tests", Guid.NewGuid().ToString("N"));

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string SettingsPath => Path.Combine(_dir, "sub", "settings.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = SettingsStore.Load(SettingsPath);
        Assert.Null(s.Window);
        Assert.Empty(s.RecentFiles);
    }

    [Fact]
    public void SaveLoad_RoundTrips()
    {
        var s = new AppSettings { Window = new WindowPlacement(10, 20, 800, 600, true) };
        s.PushRecent(@"C:\music\a.flac");
        SettingsStore.Save(SettingsPath, s);

        var r = SettingsStore.Load(SettingsPath);
        Assert.Equal(new WindowPlacement(10, 20, 800, 600, true), r.Window);
        Assert.Equal([@"C:\music\a.flac"], r.RecentFiles);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ this is not json");
        var s = SettingsStore.Load(SettingsPath);
        Assert.Null(s.Window);
    }

    [Fact]
    public void PushRecent_DedupesCaseInsensitive_MovesToFront()
    {
        var s = new AppSettings();
        s.PushRecent(@"C:\a.mp3");
        s.PushRecent(@"C:\b.mp3");
        s.PushRecent(@"C:\A.MP3");
        Assert.Equal([@"C:\A.MP3", @"C:\b.mp3"], s.RecentFiles);
    }

    [Fact]
    public void PushRecent_CapsAtMaxRecent()
    {
        var s = new AppSettings();
        for (var i = 1; i <= 15; i++) s.PushRecent($@"C:\{i}.mp3");
        Assert.Equal(AppSettings.MaxRecent, s.RecentFiles.Count);
        Assert.Equal(@"C:\15.mp3", s.RecentFiles[0]);
    }
}
