using Spektra.Core;

namespace Spektra.Tests;

public class FfmpegLocatorTests
{
    [Test]
    public async Task FindsBinariesInProbeDir()
    {
        var dir = Directory.CreateTempSubdirectory("spektra-loc").FullName;
        var exe = OperatingSystem.IsWindows() ? ".exe" : "";
        File.WriteAllText(Path.Combine(dir, "ffmpeg" + exe), "");
        File.WriteAllText(Path.Combine(dir, "ffprobe" + exe), "");

        var found = FfmpegLocator.Locate([dir], searchPath: false);
        await Assert.That(found).IsNotNull();
        await Assert.That(found!.FfmpegPath).IsEqualTo(Path.Combine(dir, "ffmpeg" + exe));
    }

    [Test]
    public async Task ReturnsNullWhenAbsent()
    {
        var empty = Directory.CreateTempSubdirectory("spektra-empty").FullName;
        await Assert.That(FfmpegLocator.Locate([empty], searchPath: false)).IsNull();
    }

    [Test]
    public async Task FindsRealBinariesViaPath()
    {
        // ffmpeg is on PATH on the dev machine (verified in plan preflight)
        await Assert.That(FfmpegLocator.Locate([], searchPath: true)).IsNotNull();
    }

    [Test]
    public async Task ClassifySource_LabelsEachProbeLocation()
    {
        var exe = OperatingSystem.IsWindows() ? ".exe" : "";
        var appDir = OperatingSystem.IsWindows() ? @"C:\Program Files\Spektra" : "/opt/spektra";
        var download = OperatingSystem.IsWindows()
            ? @"C:\Users\me\AppData\Local\Spektra\ffmpeg"
            : "/home/me/.local/share/Spektra/ffmpeg";
        var elsewhere = OperatingSystem.IsWindows() ? @"C:\tools\ffmpeg\bin" : "/usr/bin";

        await Assert.That(FfmpegLocator.ClassifySource(
            Path.Combine(appDir, "ffmpeg" + exe), appDir, download)).IsEqualTo("app folder");
        await Assert.That(FfmpegLocator.ClassifySource(
            Path.Combine(download, "ffmpeg" + exe), appDir, download)).IsEqualTo("downloaded");
        await Assert.That(FfmpegLocator.ClassifySource(
            Path.Combine(elsewhere, "ffmpeg" + exe), appDir, download)).IsEqualTo("system");
    }

    [Test]
    public async Task ClassifySource_IgnoresTrailingSeparatorAndWindowsCase()
    {
        if (!OperatingSystem.IsWindows()) return;
        // AppContext.BaseDirectory carries a trailing separator; the exe path may
        // differ only by case. Both must still resolve to "app folder".
        await Assert.That(FfmpegLocator.ClassifySource(
            @"C:\App\ffmpeg.exe", @"c:\app\", @"C:\dl")).IsEqualTo("app folder");
    }
}
