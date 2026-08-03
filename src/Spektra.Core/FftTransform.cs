using MathNet.Numerics;

namespace Spektra.Core;

/// dB magnitude spectrum of one real, already-windowed frame.
/// Not thread-safe: one instance per analysis thread.
///
/// The input is real, so this runs a HALF-SIZE complex FFT instead of feeding N
/// real samples to an N-point transform and throwing away the mirrored half.
/// Packing x[2k] and x[2k+1] as one complex sample makes the N/2-point spectrum
/// a mix of the even- and odd-indexed sub-DFTs, which one twiddle per bin
/// separates again. That plus the sqrt-free dB below took a 5-minute track from
/// 396 ms to 250 ms.
///
/// Accuracy: on broadband input this lands ~6e-5 dB from a full complex
/// transform. Do not read that as a bound. A near-null bin of a pure tone is
/// reached by cancelling large numbers, and summing them in a different order
/// moves such a bin by a few tenths of a dB, which is a property of the
/// cancellation and not of this decomposition. Every fixture's verdict and
/// cutoff was checked identical across the two paths before the switch.
public sealed class FftTransform
{
    private readonly int _size;
    private readonly Fft _half;
    private readonly Complex32[] _packed;
    private readonly float[] _cos, _sin;   // e^(-2*pi*i*k/N), one per output bin

    public int Bins { get; }

    public FftTransform(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
            throw new ArgumentException(
                $"FFT size must be a power of two >= 2, got {size}.", nameof(size));

        _size = size;
        Bins = size / 2 + 1;
        _half = Fft.OfSize(size / 2);
        _packed = new Complex32[size / 2];
        _cos = new float[Bins];
        _sin = new float[Bins];
        for (var k = 0; k < Bins; k++)
        {
            var angle = -2.0 * Math.PI * k / size;
            _cos[k] = (float)Math.Cos(angle);
            _sin[k] = (float)Math.Sin(angle);
        }
    }

    public void DbSpectrum(ReadOnlySpan<float> windowedSamples, float windowSum, Span<float> dbOut)
    {
        var half = _size / 2;
        for (var k = 0; k < half; k++)
            _packed[k] = new Complex32(windowedSamples[2 * k], windowedSamples[2 * k + 1]);

        _half.Forward(_packed);

        // Power rather than amplitude: 10*log10(|X|^2) is 20*log10(|X|) without
        // a square root per bin, so the amplitude normalization is squared here
        // to match.
        var norm = 2f / windowSum;
        var powerNorm = norm * norm;

        for (var k = 0; k < Bins; k++)
        {
            // Z[k] and conj(Z[half-k]) carry the even and odd sub-DFTs. Both
            // indices wrap at `half`, the packed spectrum being periodic there.
            var a = _packed[k == half ? 0 : k];
            var b = _packed[(half - k) % half];
            float ar = a.Real, ai = a.Imaginary;
            float br = b.Real, bi = -b.Imaginary;

            var evenR = 0.5f * (ar + br);
            var evenI = 0.5f * (ai + bi);
            var diffR = 0.5f * (ar - br);
            var diffI = 0.5f * (ai - bi);
            var oddR = diffI;          // the odd half times -i
            var oddI = -diffR;

            float wr = _cos[k], wi = _sin[k];
            var re = evenR + (wr * oddR - wi * oddI);
            var im = evenI + (wr * oddI + wi * oddR);

            dbOut[k] = Db.FromPower((re * re + im * im) * powerNorm);
        }
    }
}
