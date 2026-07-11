using Spektra.Core;

namespace Spektra.Tests;

public class DiffScoreTests
{
    [Test]
    public async Task AllZero_IsPerfectScore()
    {
        var m = DiffScore.Compute([[0f, 0f, 0f], [0f, 0f, 0f]]);
        await Assert.That(m.MeanAbsDb).IsCloseTo(0f, 0.001f);
        await Assert.That(m.RmsDb).IsCloseTo(0f, 0.001f);
        await Assert.That(m.MaxAbsDb).IsCloseTo(0f, 0.001f);
        await Assert.That(m.FractionWithinTolerance).IsCloseTo(1.0, 0.001);
    }

    [Test]
    public async Task MixedValues_AggregateCorrectly()
    {
        var m = DiffScore.Compute([[3f, -3f], [6f, 0f]], toleranceDb: 3f);
        await Assert.That(m.MeanAbsDb).IsCloseTo(3f, 0.001f);          // (3+3+6+0)/4
        await Assert.That(m.MaxAbsDb).IsCloseTo(6f, 0.001f);
        await Assert.That(m.FractionWithinTolerance).IsCloseTo(0.75, 0.001); // 3 of 4 have |d| <= 3
    }

    [Test]
    public async Task Empty_IsIdentity()
    {
        var m = DiffScore.Compute([]);
        await Assert.That(m.FractionWithinTolerance).IsCloseTo(1.0, 0.001);
        await Assert.That(m.RmsDb).IsCloseTo(0f, 0.001f);
    }
}

public class NullTestTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static string P(string file) => Path.Combine(Fixtures, file);

    [Test]
    public async Task IdenticalFiles_NullToSilence()
    {
        var r = new NullTest(Ff).Compute(
            P("chirp.wav"), null, P("chirp.wav"), null, 0, 3, 0, 44100, CancellationToken.None);
        await Assert.That(r.ResidualRmsDb < -80f).IsTrue(); // residual should be near silent
    }

    [Test]
    public async Task DifferentContent_LeavesResidual()
    {
        var r = new NullTest(Ff).Compute(
            P("chirp.wav"), null, P("chirp-lp16k.wav"), null, 0, 3, 0, 44100, CancellationToken.None);
        await Assert.That(r.ResidualRmsDb > -60f).IsTrue(); // a real difference should leave audible residual
        await Assert.That(r.NullDepthDb < r.ReferenceRmsDb - r.ResidualRmsDb + 1f).IsTrue();
    }

    [Test]
    public async Task CorrectOffset_NullsBetterThanZero()
    {
        var misaligned = new NullTest(Ff).Compute(
            P("chirp.wav"), null, P("chirp-delay50ms.wav"), null, 0, 2.5, 0, 44100, CancellationToken.None);
        var aligned = new NullTest(Ff).Compute(
            P("chirp.wav"), null, P("chirp-delay50ms.wav"), null, 0, 2.5, 0.05, 44100, CancellationToken.None);
        await Assert.That(aligned.ResidualRmsDb < misaligned.ResidualRmsDb - 6f).IsTrue(); // aligning should reduce the residual
    }
}
