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
    public void PureLowTone_IsBandLimited_NotLossy()
    {
        // Energy only near 1 kHz, dead above. That is band-limited content, not a
        // codec cutoff, so it must not be flagged Lossy.
        const int bins = 1025;
        const double hzPerBin = 22050.0 / (bins - 1);
        var col = new float[bins];
        Array.Fill(col, Db.Floor);
        var toneBin = (int)Math.Round(1000 / hzPerBin);
        col[toneBin] = -3f;
        col[toneBin - 1] = col[toneBin + 1] = -14f;
        var v = CutoffAnalyzer.Analyze([col], 44100);
        Assert.NotEqual(VerdictKind.Lossy, v.Kind);
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

    // --- Upsampling detection ---------------------------------------------

    /// One synthetic peak-hold column: `level` dB up to `cutoffHz`, then
    /// `tailLevel` up to `tailToHz`, then silence. 1025 bins matches window 2048.
    private static float[] SyntheticColumn(
        double nyquist, double cutoffHz, float level = -6f,
        float tailLevel = Db.Floor, double tailToHz = 0)
    {
        const int bins = 1025;
        var hzPerBin = nyquist / (bins - 1);
        var col = new float[bins];
        for (var k = 0; k < bins; k++)
        {
            var hz = k * hzPerBin;
            col[k] = hz <= cutoffHz ? level : hz <= tailToHz ? tailLevel : Db.Floor;
        }
        return col;
    }

    [Fact]
    public void HiResBrickWallAt22k05_IsUpsampledFrom44k1()
    {
        var v = CutoffAnalyzer.Analyze([SyntheticColumn(48000, 22050)], 96000);
        Assert.Equal(VerdictKind.Upsampled, v.Kind);
        Assert.NotNull(v.CutoffHz);
        Assert.InRange(v.CutoffHz!.Value, 21500, 22500);
        Assert.Null(v.CodecGuess);
        Assert.Contains("44.1", v.Summary);
    }

    [Fact]
    public void HiResBrickWallAt24k_IsUpsampledFrom48k()
    {
        var v = CutoffAnalyzer.Analyze([SyntheticColumn(48000, 24000)], 96000);
        Assert.Equal(VerdictKind.Upsampled, v.Kind);
        Assert.Contains("48 kHz", v.Summary);
    }

    [Fact]
    public void FullBandHiRes_IsLossless()
    {
        var v = CutoffAnalyzer.Analyze([SyntheticColumn(48000, 48000)], 96000);
        Assert.Equal(VerdictKind.Lossless, v.Kind);
    }

    [Fact]
    public void GenuineCdRate_SharpCutoff20k_StaysLossy_NotUpsampled()
    {
        // Declared 44.1 kHz with a 20 kHz wall: no base-rate match, no step-up.
        var v = CutoffAnalyzer.Analyze([SyntheticColumn(22050, 20000)], 44100);
        Assert.Equal(VerdictKind.Lossy, v.Kind);
        Assert.NotNull(v.CodecGuess);
    }

    [Fact]
    public void HiRes_SoftRolloffNear22k_IsSuspicious_NotUpsampled()
    {
        // -6 dB to 22.05 kHz, then a -65 dB shelf for ~2 kHz before silence:
        // the wall is not sharp, so this must stay Suspicious.
        var v = CutoffAnalyzer.Analyze(
            [SyntheticColumn(48000, 22050, level: -6f, tailLevel: -65f, tailToHz: 24050)], 96000);
        Assert.Equal(VerdictKind.Suspicious, v.Kind);
    }

    [Fact]
    public void FortyEightK_BrickWallAt22k05_NotUpsampled_StepUpTooSmall()
    {
        // 44.1→48 kHz is a 1.09× step: must NOT read as upsampled.
        var v = CutoffAnalyzer.Analyze([SyntheticColumn(24000, 22050)], 48000);
        Assert.Equal(VerdictKind.Lossy, v.Kind);
    }

    [Fact]
    public void SixteenKWall_OnlyUpsampledWhenContainerIsHiRes()
    {
        // 16 kHz is also the MP3-128 cutoff, so the 32 kHz base is trusted only
        // for genuinely hi-res containers.
        var hiRes = CutoffAnalyzer.Analyze([SyntheticColumn(48000, 16000)], 96000);
        Assert.Equal(VerdictKind.Upsampled, hiRes.Kind);
        Assert.Contains("32 kHz", hiRes.Summary);

        var normal = CutoffAnalyzer.Analyze([SyntheticColumn(24000, 16000)], 48000);
        Assert.Equal(VerdictKind.Lossy, normal.Kind);
    }
}
