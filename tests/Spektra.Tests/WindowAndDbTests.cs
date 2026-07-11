using Spektra.Core;

namespace Spektra.Tests;

public class WindowAndDbTests
{
    [Test]
    public async Task Hann_HasExpectedShape()
    {
        var w = WindowFunction.Hann(2048);
        await Assert.That(w.Length).IsEqualTo(2048);
        await Assert.That(w[0]).IsCloseTo(0f, 0.001f);
        await Assert.That(w[1024]).IsCloseTo(1f, 0.01f);              // ~peak at center
        await Assert.That(w[2047 - 100]).IsCloseTo(w[100], 0.0001f);  // symmetric
        await Assert.That(w.All(v => v >= 0f && v <= 1f)).IsTrue();
    }

    [Test]
    [Arguments(WindowFunctionKind.Hamming)]
    [Arguments(WindowFunctionKind.Blackman)]
    [Arguments(WindowFunctionKind.BlackmanHarris)]
    public async Task Windows_AreSymmetric_AndPeakNearCenter(WindowFunctionKind kind)
    {
        var w = WindowFunction.Create(kind, 1024);
        await Assert.That(w.Length).IsEqualTo(1024);
        await Assert.That(w[1023 - 64]).IsCloseTo(w[64], 0.0001f);  // symmetric
        await Assert.That(w[512] > w[64]).IsTrue();  // peak should be near the center
        await Assert.That(w.All(v => v >= -0.01f && v <= 1.01f)).IsTrue();
    }

    [Test]
    public async Task Create_Hann_MatchesDirectHann()
    {
        var a = WindowFunction.Create(WindowFunctionKind.Hann, 256);
        var b = WindowFunction.Hann(256);
        await Assert.That(a.SequenceEqual(b)).IsTrue();
    }

    [Test]
    public async Task Hamming_HasNonZeroEndpoints_UnlikeHann()
    {
        var hamming = WindowFunction.Hamming(1024);
        await Assert.That(hamming[0] > 0.05f).IsTrue();  // Hamming does not reach zero at the edges
        await Assert.That(hamming[0]).IsCloseTo(0.08f, 0.01f);
    }

    [Test]
    public async Task Db_ConvertsAndClamps()
    {
        await Assert.That(Db.FromAmplitude(1f)).IsCloseTo(0f, 0.001f);
        await Assert.That(Db.FromAmplitude(0.5f)).IsCloseTo(-6.02f, 0.1f);
        await Assert.That(Db.FromAmplitude(0f)).IsCloseTo(Db.Floor, 0.001f);     // silence clamps to floor
        await Assert.That(Db.FromAmplitude(1e-9f)).IsCloseTo(Db.Floor, 0.001f);  // below floor clamps
        await Assert.That(Db.FromAmplitude(1.5f)).IsCloseTo(0f, 0.001f);         // above full scale clamps to 0
    }
}
