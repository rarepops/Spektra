using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class FftTransformTests
{
    [Fact]
    public void FullScaleSine_PeaksNearZeroDbInCorrectBin()
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
        Assert.Equal(1025, fft.Bins);
        var db = new float[fft.Bins];
        fft.DbSpectrum(samples, windowSum, db);

        var peakBin = 0;
        for (var k = 1; k < db.Length; k++) if (db[k] > db[peakBin]) peakBin = k;

        // 1000 Hz / (44100/2048 Hz-per-bin) = 46.44 -> bin 46 or 47
        Assert.InRange(peakBin, 46, 47);
        Assert.True(db[peakBin] > -2f, $"peak {db[peakBin]} dB too low");   // scalloping loss < 1.5 dB
        Assert.True(db[peakBin + 20] < -40f, "leakage 20 bins away should be far down");
        Assert.True(db[500] < -60f, "far bins should be near floor");
    }
}
