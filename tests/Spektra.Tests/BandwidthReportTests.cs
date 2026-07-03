using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class BandwidthReportTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static string P(string file) => Path.Combine(Fixtures, file);

    [Fact]
    public void Analyze_FullBandFile_IsLossless()
    {
        var r = BandwidthReport.Analyze(Ff, P("chirp.wav"));
        Assert.Null(r.Error);
        Assert.NotNull(r.Metadata);
        Assert.Equal(VerdictKind.Lossless, r.Verdict!.Kind);
    }

    [Fact]
    public void Analyze_LowBitrateMp3_IsLossy()
    {
        var r = BandwidthReport.Analyze(Ff, P("chirp-mp3-64.mp3"));
        Assert.Equal(VerdictKind.Lossy, r.Verdict!.Kind);
    }

    [Fact]
    public void Analyze_MissingFile_ReturnsError()
    {
        var r = BandwidthReport.Analyze(Ff, P("does-not-exist.wav"));
        Assert.NotNull(r.Error);
        Assert.Null(r.Verdict);
    }

    [Fact]
    public void FindAudioFiles_FindsFixtures_SkipsNonAudio()
    {
        var files = BandwidthReport.FindAudioFiles(Fixtures, recursive: false).ToList();
        Assert.Contains(files, f => f.EndsWith("chirp.wav", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => f.EndsWith("chirp-mp3-64.mp3", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files, f => f.EndsWith("notaudio.txt", StringComparison.OrdinalIgnoreCase));
    }
}
