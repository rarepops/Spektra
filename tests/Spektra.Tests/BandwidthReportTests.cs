using Spektra.Core;

namespace Spektra.Tests;

public class BandwidthReportTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static string P(string file) => Path.Combine(Fixtures, file);

    [Test]
    public async Task Analyze_FullBandFile_IsLossless()
    {
        var r = BandwidthReport.Analyze(Ff, P("chirp.wav"));
        await Assert.That(r.Error).IsNull();
        await Assert.That(r.Metadata).IsNotNull();
        await Assert.That(r.Verdict!.Kind).IsEqualTo(VerdictKind.Lossless);
    }

    [Test]
    public async Task Analyze_LowBitrateMp3_IsLossy()
    {
        var r = BandwidthReport.Analyze(Ff, P("chirp-mp3-64.mp3"));
        await Assert.That(r.Verdict!.Kind).IsEqualTo(VerdictKind.Lossy);
    }

    [Test]
    public async Task Analyze_MissingFile_ReturnsError()
    {
        var r = BandwidthReport.Analyze(Ff, P("does-not-exist.wav"));
        await Assert.That(r.Error).IsNotNull();
        await Assert.That(r.Verdict).IsNull();
    }

    [Test]
    public async Task FindAudioFiles_FindsFixtures_SkipsNonAudio()
    {
        var files = BandwidthReport.FindAudioFiles(Fixtures, recursive: false).ToList();
        await Assert.That(files.Any(f => f.EndsWith("chirp.wav", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(files.Any(f => f.EndsWith("chirp-mp3-64.mp3", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(files.Any(f => f.EndsWith("notaudio.txt", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }
}
