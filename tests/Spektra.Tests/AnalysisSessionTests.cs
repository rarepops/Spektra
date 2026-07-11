using Spektra.Core;

namespace Spektra.Tests;

public class AnalysisSessionTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Test]
    public async Task FlacSine_EndToEnd_PeaksAt1kHz()
    {
        var session = new AnalysisSession(FfmpegLocator.Locate([])!);
        var path = Path.Combine(Fixtures, "sine-1khz.flac");

        var meta = session.ReadMetadata(path);
        var settings = new SpectrogramSettings();
        var columns = session.AnalyzeColumns(path, meta, settings, CancellationToken.None).ToList();

        await Assert.That(columns.Count).IsBetween(120, 130);
        var mid = columns[columns.Count / 2];
        var peak = 0;
        for (var k = 1; k < mid.Length; k++) if (mid[k] > mid[peak]) peak = k;
        await Assert.That(peak).IsBetween(46, 47);
    }

    [Test]
    public async Task Segment_ProducesProportionalColumns()
    {
        var session = new AnalysisSession(FfmpegLocator.Locate([])!);
        var path = Path.Combine(Fixtures, "sine-1khz.wav");
        var meta = session.ReadMetadata(path);
        var cols = session.AnalyzeColumns(path, meta, new SpectrogramSettings(),
            CancellationToken.None,
            new DecodeOptions(Start: TimeSpan.FromSeconds(1), Duration: TimeSpan.FromSeconds(1)))
            .Count();
        await Assert.That(cols).IsBetween(35, 46); // (44100 - 2048) / 1024 + 1 ≈ 42
    }
}
