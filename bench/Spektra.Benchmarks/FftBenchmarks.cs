using BenchmarkDotNet.Attributes;
using Spektra.Core;

namespace Spektra.Benchmarks;

/// FftTransform.DbSpectrum: the innermost DSP kernel, run once per column.
[MemoryDiagnoser]
public class FftBenchmarks
{
    [Params(1024, 2048, 4096)]
    public int WindowSize { get; set; }

    private FftTransform _fft = null!;
    private float[] _windowed = null!;
    private float[] _dbOut = null!;
    private float _windowSum;

    [GlobalSetup]
    public void Setup()
    {
        var window = WindowFunction.Hann(WindowSize);
        _windowSum = 0f;
        foreach (var v in window) _windowSum += v;

        var signal = Signal.Sine(WindowSize, freqHz: 1000, sampleRate: 44100);
        _windowed = new float[WindowSize];
        for (var n = 0; n < WindowSize; n++) _windowed[n] = signal[n] * window[n];

        _fft = new FftTransform(WindowSize);
        _dbOut = new float[_fft.Bins];
    }

    [Benchmark]
    public float DbSpectrum()
    {
        _fft.DbSpectrum(_windowed, _windowSum, _dbOut);
        return _dbOut[^1];
    }
}
