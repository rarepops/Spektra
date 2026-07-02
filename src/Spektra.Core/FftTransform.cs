using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;

namespace Spektra.Core;

/// Not thread-safe: one instance per analysis thread.
public sealed class FftTransform(int size)
{
    private readonly Complex32[] _buffer = new Complex32[size];

    public int Bins { get; } = size / 2 + 1;

    public void DbSpectrum(ReadOnlySpan<float> windowedSamples, float windowSum, Span<float> dbOut)
    {
        for (var n = 0; n < _buffer.Length; n++)
            _buffer[n] = new Complex32(windowedSamples[n], 0f);

        Fourier.Forward(_buffer, FourierOptions.Matlab); // unscaled forward transform

        var norm = 2f / windowSum;
        for (var k = 0; k < Bins; k++)
            dbOut[k] = Db.FromAmplitude(_buffer[k].Magnitude * norm);
    }
}
