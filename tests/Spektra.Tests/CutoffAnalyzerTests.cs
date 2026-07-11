using Spektra.Core;

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

    [Test]
    public async Task BroadbandPcmNoise_IsFullBandLossless()
    {
        var v = CutoffAnalyzer.Analyze(Columns("noise.wav"), 44100);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Lossless);
        await Assert.That(v.CutoffHz).IsNull();
    }

    [Test]
    public async Task FullBandChirp_IsLossless()
    {
        var v = CutoffAnalyzer.Analyze(Columns("chirp.wav"), 44100);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Lossless);
    }

    [Test]
    public async Task LowBitrateMp3_HasBrickWallCutoff()
    {
        var v = CutoffAnalyzer.Analyze(Columns("chirp-mp3-64.mp3"), 44100);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Lossy);
        await Assert.That(v.CutoffHz).IsNotNull();
        await Assert.That(v.CutoffHz!.Value).IsBetween(14000, 18500);
        await Assert.That(v.CodecGuess).IsNotNull();
    }

    [Test]
    public async Task GradualRolloff_IsSuspicious_NotLossy()
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
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Suspicious);
        await Assert.That(v.CutoffHz).IsNotNull();
    }

    [Test]
    public async Task PureLowTone_IsBandLimited_NotLossy()
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
        await Assert.That(v.Kind).IsNotEqualTo(VerdictKind.Lossy);
    }

    [Test]
    public async Task Silence_IsUnknown()
    {
        var col = new float[1025];
        Array.Fill(col, Db.Floor);
        var v = CutoffAnalyzer.Analyze([col], 44100);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Unknown);
    }

    [Test]
    public async Task EmptyColumns_IsUnknown()
    {
        var v = CutoffAnalyzer.Analyze([], 44100);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Unknown);
    }

    [Test]
    public async Task PeakHold_TakesPerBinMaximum()
    {
        var peak = CutoffAnalyzer.PeakHold([[-90f, -10f, -120f], [-30f, -80f, -5f]]);
        await Assert.That(peak[0]).IsEqualTo(-30f);
        await Assert.That(peak[1]).IsEqualTo(-10f);
        await Assert.That(peak[2]).IsEqualTo(-5f);
    }

    [Test]
    [Arguments(21000, "MP3 320 / AAC 256+")]
    [Arguments(16000, "MP3 128 / AAC ~128")]
    [Arguments(11000, "MP3 ~64-80")]
    public async Task GuessCodec_MapsCutoffToLikelyFormat(double hz, string expected) =>
        await Assert.That(CutoffAnalyzer.GuessCodec(hz)).IsEqualTo(expected);

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

    [Test]
    public async Task HiResBrickWallAt22k05_IsUpsampledFrom44k1()
    {
        var v = CutoffAnalyzer.Analyze([SyntheticColumn(48000, 22050)], 96000);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Upsampled);
        await Assert.That(v.CutoffHz).IsNotNull();
        await Assert.That(v.CutoffHz!.Value).IsBetween(21500, 22500);
        await Assert.That(v.CodecGuess).IsNull();
        await Assert.That(v.Summary).Contains("44.1");
    }

    [Test]
    public async Task HiResBrickWallAt24k_IsUpsampledFrom48k()
    {
        var v = CutoffAnalyzer.Analyze([SyntheticColumn(48000, 24000)], 96000);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Upsampled);
        await Assert.That(v.Summary).Contains("48 kHz");
    }

    [Test]
    public async Task FullBandHiRes_IsLossless()
    {
        var v = CutoffAnalyzer.Analyze([SyntheticColumn(48000, 48000)], 96000);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Lossless);
    }

    [Test]
    public async Task GenuineCdRate_SharpCutoff20k_StaysLossy_NotUpsampled()
    {
        // Declared 44.1 kHz with a 20 kHz wall: no base-rate match, no step-up.
        var v = CutoffAnalyzer.Analyze([SyntheticColumn(22050, 20000)], 44100);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Lossy);
        await Assert.That(v.CodecGuess).IsNotNull();
    }

    [Test]
    public async Task HiRes_SoftRolloffNear22k_IsSuspicious_NotUpsampled()
    {
        // -6 dB to 22.05 kHz, then a -65 dB shelf for ~2 kHz before silence:
        // the wall is not sharp, so this must stay Suspicious.
        var v = CutoffAnalyzer.Analyze(
            [SyntheticColumn(48000, 22050, level: -6f, tailLevel: -65f, tailToHz: 24050)], 96000);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Suspicious);
    }

    [Test]
    public async Task FortyEightK_BrickWallAt22k05_NotUpsampled_StepUpTooSmall()
    {
        // 44.1→48 kHz is a 1.09× step: must NOT read as upsampled.
        var v = CutoffAnalyzer.Analyze([SyntheticColumn(24000, 22050)], 48000);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Lossy);
    }

    /// Emulates ffmpeg's default (swr) resampler: passband to `sourceNyquistHz`,
    /// then a lazy ~14 dB/kHz skirt down to -60 dB, then a cliff to silence.
    /// Geometry measured on real swr 44.1→96 kHz output (probe, 2026-07-06),
    /// where the peak-55 dB edge overshoots the source Nyquist by ~15%.
    private static float[] LazySkirtColumn(double nyquist, double sourceNyquistHz)
    {
        const int bins = 1025;
        var hzPerBin = nyquist / (bins - 1);
        var col = new float[bins];
        for (var k = 0; k < bins; k++)
        {
            var hz = k * hzPerBin;
            var level = hz <= sourceNyquistHz
                ? -6f
                : -6f - 14f * (float)((hz - sourceNyquistHz) / 1000.0);
            col[k] = level < -60f ? Db.Floor : level;
        }
        return col;
    }

    [Test]
    public async Task LazyResamplerSkirt_44k1To96k_IsUpsampled()
    {
        var v = CutoffAnalyzer.Analyze([LazySkirtColumn(48000, 22050)], 96000);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Upsampled);
        await Assert.That(v.Summary).Contains("44.1");
    }

    [Test]
    public async Task LazyResamplerSkirt_48kTo96k_NamesThe48kSource()
    {
        var v = CutoffAnalyzer.Analyze([LazySkirtColumn(48000, 24000)], 96000);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Upsampled);
        await Assert.That(v.Summary).Contains("48 kHz");
    }

    [Test]
    public async Task SixteenKWall_OnlyUpsampledWhenContainerIsHiRes()
    {
        // 16 kHz is also the MP3-128 cutoff, so the 32 kHz base is trusted only
        // for genuinely hi-res containers.
        var hiRes = CutoffAnalyzer.Analyze([SyntheticColumn(48000, 16000)], 96000);
        await Assert.That(hiRes.Kind).IsEqualTo(VerdictKind.Upsampled);
        await Assert.That(hiRes.Summary).Contains("32 kHz");

        var normal = CutoffAnalyzer.Analyze([SyntheticColumn(24000, 16000)], 48000);
        await Assert.That(normal.Kind).IsEqualTo(VerdictKind.Lossy);
    }
}
