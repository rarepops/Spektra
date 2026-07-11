using Spektra.Core;

namespace Spektra.Tests;

public class FftTransformTests
{
    [Test]
    public async Task FullScaleSine_PeaksNearZeroDbInCorrectBin()
    {
        const int size = 2048, sampleRate = 44100;
        const float freq = 1000f;
        var window = WindowFunction.Hann(size);
        var windowSum = 0f;
        foreach (var v in window) windowSum += v;

        var samples = new float[size];
        for (var n = 0; n < size; n++)
            samples[n] = MathF.Sin(2f * MathF.PI * freq * n / sampleRate) * window[n];

        var fft = new FftTransform(size);
        await Assert.That(fft.Bins).IsEqualTo(1025);
        var db = new float[fft.Bins];
        fft.DbSpectrum(samples, windowSum, db);

        var peakBin = 0;
        for (var k = 1; k < db.Length; k++) if (db[k] > db[peakBin]) peakBin = k;

        // 1000 Hz / (44100/2048 Hz-per-bin) = 46.44 -> bin 46 or 47
        await Assert.That(peakBin).IsBetween(46, 47);
        await Assert.That(db[peakBin] > -2f).IsTrue();   // scalloping loss < 1.5 dB
        await Assert.That(db[peakBin + 20] < -40f).IsTrue(); // leakage 20 bins away should be far down
        await Assert.That(db[500] < -60f).IsTrue(); // far bins should be near floor
    }
}
