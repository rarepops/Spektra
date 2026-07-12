using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using Spektra.Core;

namespace Spektra.Tests;

public class FftTests
{
    private static readonly int[] Sizes = [1024, 2048, 4096];

    private static Complex32[] RandomComplex(int n, int seed)
    {
        var rng = new Random(seed);
        var a = new Complex32[n];
        for (var i = 0; i < n; i++)
            a[i] = new Complex32((float)(rng.NextDouble() * 2 - 1), (float)(rng.NextDouble() * 2 - 1));
        return a;
    }

    [Test]
    public async Task Forward_MatchesMathNet()
    {
        foreach (var size in Sizes)
        {
            var mine = RandomComplex(size, 42);
            var reference = (Complex32[])mine.Clone();
            Fft.OfSize(size).Forward(mine);
            Fourier.Forward(reference, FourierOptions.Matlab);

            var worst = 0.0;
            for (var k = 0; k < size; k++)
            {
                var err = (mine[k] - reference[k]).Magnitude;
                var tol = 1e-3f * reference[k].Magnitude + 0.05f;
                worst = Math.Max(worst, err - tol); // > 0 means a bin exceeded tolerance
            }
            await Assert.That(worst <= 0).IsTrue();
        }
    }

    [Test]
    public async Task Inverse_MatchesMathNet()
    {
        foreach (var size in Sizes)
        {
            var mine = RandomComplex(size, 99);
            var reference = (Complex32[])mine.Clone();
            Fft.OfSize(size).Inverse(mine);
            Fourier.Inverse(reference, FourierOptions.Matlab);

            var worst = 0.0;
            for (var k = 0; k < size; k++)
            {
                var err = (mine[k] - reference[k]).Magnitude;
                var tol = 1e-3f * reference[k].Magnitude + 1e-4f;
                worst = Math.Max(worst, err - tol);
            }
            await Assert.That(worst <= 0).IsTrue();
        }
    }

    [Test]
    public async Task ForwardThenInverse_RoundTrips()
    {
        foreach (var size in Sizes)
        {
            var input = RandomComplex(size, 7);
            var buf = (Complex32[])input.Clone();
            var fft = Fft.OfSize(size);
            fft.Forward(buf);
            fft.Inverse(buf);

            var worst = 0.0;
            for (var k = 0; k < size; k++) worst = Math.Max(worst, (buf[k] - input[k]).Magnitude);
            await Assert.That(worst < 1e-2).IsTrue();
        }
    }

    [Test]
    public async Task OfSize_RejectsNonPowerOfTwo()
    {
        var threw = false;
        try { Fft.OfSize(1000); }
        catch (ArgumentException) { threw = true; }
        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task OfSize_ReturnsSameCachedInstance()
    {
        await Assert.That(ReferenceEquals(Fft.OfSize(2048), Fft.OfSize(2048))).IsTrue();
    }
}
