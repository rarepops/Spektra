using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class CutoffAnalyzerTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static string P(string file) => Path.Combine(Fixtures, file);

    private static IReadOnlyList<float[]> Columns(string file, int window = 2048)
    {
        var session = new AnalysisSession(Ff);
        var meta = session.ReadMetadata(P(file));
        var settings = new SpectrogramSettings(WindowSize: window);
        return session.AnalyzeColumns(P(file), meta, settings, CancellationToken.None).ToList();
    }

    [Fact]
    public void BroadbandPcmNoise_IsFullBandLossless()
    {
        var v = CutoffAnalyzer.Analyze(Columns("noise.wav"), 44100);
        Assert.Equal(VerdictKind.Lossless, v.Kind);
        Assert.Null(v.CutoffHz);
    }

    [Fact]
    public void FullBandChirp_IsLossless()
    {
        var v = CutoffAnalyzer.Analyze(Columns("chirp.wav"), 44100);
        Assert.Equal(VerdictKind.Lossless, v.Kind);
    }

    [Fact]
    public void LowBitrateMp3_HasBrickWallCutoff()
    {
        var v = CutoffAnalyzer.Analyze(Columns("chirp-mp3-64.mp3"), 44100);
        Assert.Equal(VerdictKind.Lossy, v.Kind);
        Assert.NotNull(v.CutoffHz);
        Assert.InRange(v.CutoffHz!.Value, 14000, 18500);
        Assert.NotNull(v.CodecGuess);
    }

    [Fact]
    public void GradualRolloff_IsSuspicious_NotLossy()
    {
        // Strong to ~13 kHz, then a slow decline into the floor (a natural-style
        // rolloff): a cutoff exists below Nyquist but the transition is wide.
        const int bins = 1025;
        const double nyquist = 22050.0, hzPerBin = nyquist / (bins - 1);
        var col = new float[bins];
        for (var k = 0; k < bins; k++)
        {
            var hz = k * hzPerBin;
            col[k] = hz <= 13000 ? -6f : Math.Max(Db.Floor, -6f - (float)((hz - 13000) / 1000.0) * 12f);
        }
        var v = CutoffAnalyzer.Analyze([col], 44100);
        Assert.Equal(VerdictKind.Suspicious, v.Kind);
        Assert.NotNull(v.CutoffHz);
    }

    [Fact]
    public void Silence_IsUnknown()
    {
        var col = new float[1025];
        Array.Fill(col, Db.Floor);
        var v = CutoffAnalyzer.Analyze([col], 44100);
        Assert.Equal(VerdictKind.Unknown, v.Kind);
    }

    [Fact]
    public void EmptyColumns_IsUnknown()
    {
        var v = CutoffAnalyzer.Analyze([], 44100);
        Assert.Equal(VerdictKind.Unknown, v.Kind);
    }

    [Fact]
    public void PeakHold_TakesPerBinMaximum()
    {
        var peak = CutoffAnalyzer.PeakHold([[-90f, -10f, -120f], [-30f, -80f, -5f]]);
        Assert.Equal(-30f, peak[0]);
        Assert.Equal(-10f, peak[1]);
        Assert.Equal(-5f, peak[2]);
    }

    [Theory]
    [InlineData(21000, "MP3 320 / AAC 256+")]
    [InlineData(16000, "MP3 128 / AAC ~128")]
    [InlineData(11000, "MP3 ~64-80")]
    public void GuessCodec_MapsCutoffToLikelyFormat(double hz, string expected) =>
        Assert.Equal(expected, CutoffAnalyzer.GuessCodec(hz));
}
