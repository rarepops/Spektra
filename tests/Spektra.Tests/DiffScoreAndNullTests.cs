using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class DiffScoreTests
{
    [Fact]
    public void AllZero_IsPerfectScore()
    {
        var m = DiffScore.Compute([[0f, 0f, 0f], [0f, 0f, 0f]]);
        Assert.Equal(0f, m.MeanAbsDb, 3);
        Assert.Equal(0f, m.RmsDb, 3);
        Assert.Equal(0f, m.MaxAbsDb, 3);
        Assert.Equal(1.0, m.FractionWithinTolerance, 3);
    }

    [Fact]
    public void MixedValues_AggregateCorrectly()
    {
        var m = DiffScore.Compute([[3f, -3f], [6f, 0f]], toleranceDb: 3f);
        Assert.Equal(3f, m.MeanAbsDb, 3);          // (3+3+6+0)/4
        Assert.Equal(6f, m.MaxAbsDb, 3);
        Assert.Equal(0.75, m.FractionWithinTolerance, 3); // 3 of 4 have |d| <= 3
    }

    [Fact]
    public void Empty_IsIdentity()
    {
        var m = DiffScore.Compute([]);
        Assert.Equal(1.0, m.FractionWithinTolerance, 3);
        Assert.Equal(0f, m.RmsDb, 3);
    }
}

public class NullTestTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static string P(string file) => Path.Combine(Fixtures, file);

    [Fact]
    public void IdenticalFiles_NullToSilence()
    {
        var r = new NullTest(Ff).Compute(
            P("chirp.wav"), null, P("chirp.wav"), null, 0, 3, 0, 44100, CancellationToken.None);
        Assert.True(r.ResidualRmsDb < -80f, $"residual should be near silent, was {r.ResidualRmsDb}");
    }

    [Fact]
    public void DifferentContent_LeavesResidual()
    {
        var r = new NullTest(Ff).Compute(
            P("chirp.wav"), null, P("chirp-lp16k.wav"), null, 0, 3, 0, 44100, CancellationToken.None);
        Assert.True(r.ResidualRmsDb > -60f, $"a real difference should leave audible residual, was {r.ResidualRmsDb}");
        Assert.True(r.NullDepthDb < r.ReferenceRmsDb - r.ResidualRmsDb + 1f);
    }

    [Fact]
    public void CorrectOffset_NullsBetterThanZero()
    {
        var misaligned = new NullTest(Ff).Compute(
            P("chirp.wav"), null, P("chirp-delay50ms.wav"), null, 0, 2.5, 0, 44100, CancellationToken.None);
        var aligned = new NullTest(Ff).Compute(
            P("chirp.wav"), null, P("chirp-delay50ms.wav"), null, 0, 2.5, 0.05, 44100, CancellationToken.None);
        Assert.True(aligned.ResidualRmsDb < misaligned.ResidualRmsDb - 6f,
            $"aligning should reduce the residual (aligned {aligned.ResidualRmsDb}, misaligned {misaligned.ResidualRmsDb})");
    }
}
