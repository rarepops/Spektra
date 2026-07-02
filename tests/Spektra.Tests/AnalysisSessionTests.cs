using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class AnalysisSessionTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public void FlacSine_EndToEnd_PeaksAt1kHz()
    {
        var session = new AnalysisSession(FfmpegLocator.Locate([])!);
        var path = Path.Combine(Fixtures, "sine-1khz.flac");

        var meta = session.ReadMetadata(path);
        var settings = new SpectrogramSettings();
        var columns = session.AnalyzeColumns(path, meta, settings, CancellationToken.None).ToList();

        Assert.InRange(columns.Count, 120, 130);
        var mid = columns[columns.Count / 2];
        var peak = 0;
        for (var k = 1; k < mid.Length; k++) if (mid[k] > mid[peak]) peak = k;
        Assert.InRange(peak, 46, 47);
    }
}
