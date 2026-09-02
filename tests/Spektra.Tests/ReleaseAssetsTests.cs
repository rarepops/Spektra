using Spektra.Core;

namespace Spektra.Tests;

public class ReleaseAssetsTests
{
    private const string Base = "https://github.com/rarepops/Spektra/releases/download/v0.23.0/";

    /// The v0.23.0 asset list as published, plus the zipped installer that
    /// joins from the next release on.
    private static readonly IReadOnlyList<ReleaseAsset> Published =
    [
        Asset("SHA256SUMS.txt", 1),
        Asset("Spektra-0.23.0-Setup.msi", 76),
        Asset("Spektra-0.23.0-Setup.zip", 76),
        Asset("Spektra-0.23.0-win-x64.zip", 44),
        Asset("spektra-cli-0.23.0-linux-x64.zip", 32),
        Asset("spektra-cli-0.23.0-osx-arm64.zip", 30),
        Asset("spektra-cli-0.23.0-osx-x64.zip", 32),
        Asset("spektra-cli-0.23.0-win-x64.zip", 32),
    ];

    private static ReleaseAsset Asset(string name, long mb) => new(name, Base + name, mb << 20);

    private static ReleaseTarget Windows(bool installed, bool arm64 = false) =>
        new(ReleasePlatform.Windows, arm64, installed);

    [Test]
    public async Task Pick_WindowsInstalled_TakesTheInstaller_NotItsZip()
    {
        var pick = ReleaseAssets.Pick(Published, Windows(installed: true));
        await Assert.That(pick?.Name).IsEqualTo("Spektra-0.23.0-Setup.msi");
    }

    [Test]
    public async Task Pick_WindowsPortable_TakesTheDesktopZip_NotTheCliZip()
    {
        var pick = ReleaseAssets.Pick(Published, Windows(installed: false));
        await Assert.That(pick?.Name).IsEqualTo("Spektra-0.23.0-win-x64.zip");
    }

    [Test]
    public async Task Pick_WindowsOnArm64_StillTakesTheX64Build()
    {
        // Only x64 is published for Windows; it runs under emulation.
        var pick = ReleaseAssets.Pick(Published, Windows(installed: true, arm64: true));
        await Assert.That(pick?.Name).IsEqualTo("Spektra-0.23.0-Setup.msi");
    }

    [Test]
    public async Task Pick_Linux_TakesTheCliZip()
    {
        var target = new ReleaseTarget(ReleasePlatform.Linux, Arm64: false, Installed: false);
        var pick = ReleaseAssets.Pick(Published, target);
        await Assert.That(pick?.Name).IsEqualTo("spektra-cli-0.23.0-linux-x64.zip");
    }

    [Test]
    [Arguments(true, "spektra-cli-0.23.0-osx-arm64.zip")]
    [Arguments(false, "spektra-cli-0.23.0-osx-x64.zip")]
    public async Task Pick_MacOS_MatchesTheArchitecture(bool arm64, string expected)
    {
        var target = new ReleaseTarget(ReleasePlatform.MacOS, arm64, Installed: false);
        var pick = ReleaseAssets.Pick(Published, target);
        await Assert.That(pick?.Name).IsEqualTo(expected);
    }

    [Test]
    public async Task Pick_UnknownPlatform_IsNull()
    {
        var target = new ReleaseTarget(ReleasePlatform.Other, Arm64: false, Installed: false);
        await Assert.That(ReleaseAssets.Pick(Published, target)).IsNull();
    }

    [Test]
    public async Task Pick_NothingMatching_IsNull()
    {
        IReadOnlyList<ReleaseAsset> assets = [Asset("SHA256SUMS.txt", 1), Asset("notes.txt", 1)];
        await Assert.That(ReleaseAssets.Pick(assets, Windows(installed: true))).IsNull();
    }

    [Test]
    public async Task Pick_MatchesNamesRegardlessOfCase()
    {
        IReadOnlyList<ReleaseAsset> assets = [Asset("SPEKTRA-0.23.0-SETUP.MSI", 76)];
        await Assert.That(ReleaseAssets.Pick(assets, Windows(installed: true))?.Name)
            .IsEqualTo("SPEKTRA-0.23.0-SETUP.MSI");
    }

    [Test]
    public async Task Pick_EmptyList_IsNull()
    {
        await Assert.That(ReleaseAssets.Pick([], Windows(installed: true))).IsNull();
    }

    [Test]
    public async Task Checksums_FindsTheSumsFile()
    {
        await Assert.That(ReleaseAssets.Checksums(Published)?.Name).IsEqualTo("SHA256SUMS.txt");
    }

    [Test]
    public async Task Checksums_Missing_IsNull()
    {
        IReadOnlyList<ReleaseAsset> assets = [Asset("Spektra-0.23.0-Setup.msi", 76)];
        await Assert.That(ReleaseAssets.Checksums(assets)).IsNull();
    }

    [Test]
    [Arguments(@"C:\Program Files\Spektra\", true)]
    [Arguments(@"C:\Program Files\Spektra", true)]
    [Arguments(@"c:\program files\spektra\", true)]
    [Arguments(@"C:\Program Files (x86)\Spektra\", true)]
    [Arguments(@"C:\Program Files Portable\Spektra\", false)]
    [Arguments(@"D:\Tools\Spektra\", false)]
    [Arguments(@"C:\Users\me\Downloads\Spektra-0.23.0-win-x64\", false)]
    public async Task IsInstalled_MeansUnderAProgramFilesFolder(string exeDir, bool expected)
    {
        string[] programFiles = [@"C:\Program Files", @"C:\Program Files (x86)"];
        await Assert.That(ReleaseTarget.IsInstalled(exeDir, programFiles)).IsEqualTo(expected);
    }

    [Test]
    public async Task IsInstalled_ToleratesATrailingSeparatorOnTheProgramFilesPath()
    {
        await Assert.That(ReleaseTarget.IsInstalled(@"C:\Program Files\Spektra\", [@"C:\Program Files\"])).IsTrue();
    }

    [Test]
    public async Task IsInstalled_IgnoresEmptyProgramFilesEntries()
    {
        // Environment.GetFolderPath answers "" off Windows; an empty prefix
        // must not match every path.
        await Assert.That(ReleaseTarget.IsInstalled(@"D:\Tools\Spektra\", ["", "  "])).IsFalse();
    }
}
