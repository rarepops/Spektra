using System.Collections.Concurrent;
using MathNet.Numerics;

namespace Spektra.Core;

/// Zero-allocation in-place radix-2 FFT for a fixed power-of-two size.
/// Precomputes the bit-reversal permutation and twiddle factors once, so the
/// transform runs with no heap allocation. Scaling matches MathNet's
/// FourierOptions.Matlab: Forward is unscaled, Inverse multiplies by 1/n.
/// Immutable after construction, so one cached plan is safe to share across
/// threads (each caller transforms its own buffer).
public sealed class Fft
{
    private static readonly ConcurrentDictionary<int, Fft> Cache = new();

    private readonly int _n;
    private readonly int[] _reversed;
    private readonly float[] _cos; // twiddle real part, length n/2
    private readonly float[] _sin; // twiddle imag part, length n/2

    private Fft(int n)
    {
        _n = n;
        var bits = 0;
        while (1 << bits < n) bits++;

        _reversed = new int[n];
        for (var i = 0; i < n; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++)
                if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
            _reversed[i] = r;
        }

        _cos = new float[n / 2];
        _sin = new float[n / 2];
        for (var k = 0; k < n / 2; k++)
        {
            var angle = -2.0 * Math.PI * k / n; // e^{-2 pi i k / n}, the forward twiddle
            _cos[k] = (float)Math.Cos(angle);
            _sin[k] = (float)Math.Sin(angle);
        }
    }

    /// Returns the cached plan for `size`, which must be a power of two >= 1.
    public static Fft OfSize(int size)
    {
        if (size < 1 || (size & (size - 1)) != 0)
            throw new ArgumentException($"FFT size must be a power of two, got {size}.", nameof(size));
        return Cache.GetOrAdd(size, static n => new Fft(n));
    }

    /// In-place forward DFT (unscaled), matching MathNet FourierOptions.Matlab.
    public void Forward(Span<Complex32> buffer) => Transform(buffer, inverse: false);

    /// In-place inverse DFT scaled by 1/n, matching MathNet FourierOptions.Matlab.
    public void Inverse(Span<Complex32> buffer)
    {
        Transform(buffer, inverse: true);
        var scale = 1f / _n;
        for (var i = 0; i < _n; i++)
            buffer[i] = new Complex32(buffer[i].Real * scale, buffer[i].Imaginary * scale);
    }

    private void Transform(Span<Complex32> buffer, bool inverse)
    {
        if (buffer.Length != _n)
            throw new ArgumentException($"buffer length {buffer.Length} != FFT size {_n}.", nameof(buffer));

        for (var i = 0; i < _n; i++)
        {
            var j = _reversed[i];
            if (j > i) (buffer[j], buffer[i]) = (buffer[i], buffer[j]);
        }

        for (var len = 2; len <= _n; len <<= 1)
        {
            var half = len >> 1;
            var step = _n / len; // stride into the twiddle table
            for (var start = 0; start < _n; start += len)
            {
                for (var k = 0; k < half; k++)
                {
                    var tw = k * step;
                    var wr = _cos[tw];
                    var wi = inverse ? -_sin[tw] : _sin[tw]; // conjugate twiddle for the inverse
                    var even = start + k;
                    var odd = start + k + half;
                    var er = buffer[even].Real;
                    var ei = buffer[even].Imaginary;
                    var or = buffer[odd].Real;
                    var oi = buffer[odd].Imaginary;
                    var tr = wr * or - wi * oi;
                    var ti = wr * oi + wi * or;
                    buffer[even] = new Complex32(er + tr, ei + ti);
                    buffer[odd] = new Complex32(er - tr, ei - ti);
                }
            }
        }
    }
}
