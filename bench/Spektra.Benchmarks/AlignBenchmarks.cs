using BenchmarkDotNet.Attributes;
using Spektra.Core;

namespace Spektra.Benchmarks;

/// The compare path's heavy compute: FFT cross-correlation (Aligner.EstimateLag)
/// and the piecewise drift estimate (DriftAligner.EstimateDrift) over two 16 kHz
/// signals where B is a delayed copy of A. Constants mirror the Aligner/
/// DriftAligner defaults so the sizes match real compare runs.
[MemoryDiagnoser]
public class AlignBenchmarks
{
    private const int SampleRate = 16000;      // Aligner's default analysis rate
    private const int MaxLag = SampleRate * 2; // +/-2 s, Aligner's default search radius
    private const int Segments = 6;            // Aligner's default drift segment count
    private const int DelaySamples = 800;      // 50 ms known lag

    [Params(10, 30)]
    public int WindowSeconds { get; set; }

    private float[] _a = null!;
    private float[] _b = null!;

    [GlobalSetup]
    public void Setup()
    {
        _a = Signal.Noise(SampleRate * WindowSeconds, seed: 7);
        _b = Signal.Delay(_a, DelaySamples);
    }

    [Benchmark]
    public int EstimateLag() => Aligner.EstimateLag(_a, _b, MaxLag).Lag;

    [Benchmark]
    public int EstimateDrift() => DriftAligner.EstimateDrift(_a, _b, Segments, MaxLag).Count;
}
