using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class SpectralDiffTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static AudioMetadata Meta(string file) =>
        new AnalysisSession(Ff).ReadMetadata(Path.Combine(Fixtures, file));
    private static string P(string file) => Path.Combine(Fixtures, file);

    [Fact]
    public void Identical_Inputs_ProduceZeroDiff()
    {
        var meta = Meta("chirp.wav");
        var cols = new SpectralDiff(Ff).Compute(
            P("chirp.wav"), meta, null, P("chirp.wav"), meta, null,
            0, 3, 0, 256, 2048, CancellationToken.None);
        var maxAbs = cols.SelectMany(c => c).Max(MathF.Abs);
        Assert.True(maxAbs < 0.5f, $"identical diff should be ~0, was {maxAbs}");
    }

    [Fact]
    public void FullBand_vs_LowPassed_IsPositiveAboveCutoff()
    {
        var cols = new SpectralDiff(Ff).Compute(
            P("chirp.wav"), Meta("chirp.wav"), null,
            P("chirp-lp16k.wav"), Meta("chirp-lp16k.wav"), null,
            0, 3, 0, 256, 2048, CancellationToken.None);
        // 44100 Hz, window 2048 → 21.5 Hz/bin. bin 820 ≈ 17.6 kHz (deep in B's
        // stopband); bin 350 ≈ 7.5 kHz (both share the chirp).
        var above = cols.SelectMany(c => c.Skip(820)).ToList();
        Assert.True(above.Max() > 25f, $"expected strong positive diff above cutoff, max {above.Max()}");
        Assert.True(above.Min() > -10f, $"B must not exceed A above cutoff, min {above.Min()}");
        var below = cols.SelectMany(c => c.Take(350)).ToList();
        Assert.True(below.Max() < 12f && below.Min() > -12f,
            $"below-cutoff diff should be small (max {below.Max()}, min {below.Min()})");
    }
}
