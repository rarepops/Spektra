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
}
