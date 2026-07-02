using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class FfmpegLocatorTests
{
    [Fact]
    public void FindsBinariesInProbeDir()
    {
        var dir = Directory.CreateTempSubdirectory("spektra-loc").FullName;
        var exe = OperatingSystem.IsWindows() ? ".exe" : "";
        File.WriteAllText(Path.Combine(dir, "ffmpeg" + exe), "");
        File.WriteAllText(Path.Combine(dir, "ffprobe" + exe), "");

        var found = FfmpegLocator.Locate([dir], searchPath: false);
        Assert.NotNull(found);
        Assert.Equal(Path.Combine(dir, "ffmpeg" + exe), found!.FfmpegPath);
    }

    [Fact]
    public void ReturnsNullWhenAbsent()
    {
        var empty = Directory.CreateTempSubdirectory("spektra-empty").FullName;
        Assert.Null(FfmpegLocator.Locate([empty], searchPath: false));
    }

    [Fact]
    public void FindsRealBinariesViaPath()
    {
        // ffmpeg is on PATH on the dev machine (verified in plan preflight)
        Assert.NotNull(FfmpegLocator.Locate([], searchPath: true));
    }
}
