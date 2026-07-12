using BenchmarkDotNet.Attributes;
using Spektra.Core;

namespace Spektra.Benchmarks;

/// CutoffAnalyzer.Analyze over ~2000 synthetic columns shaped like a lossy
/// brick-wall cutoff (bins = WindowSize / 2 + 1). Exercises PeakHold + the
/// verdict scan; the shaped input resolves to a Lossy verdict.
[MemoryDiagnoser]
public class CutoffBenchmarks
{
    private const int SampleRate = 44100;
    private const int WindowSize = 2048;
    private const int ColumnCount = 2000;

    private float[][] _columns = null!;

    [GlobalSetup]
    public void Setup()
    {
        var bins = WindowSize / 2 + 1;
        _columns = Signal.BrickWallColumns(ColumnCount, bins, SampleRate, cutoffHz: 16000, seed: 99);
    }

    [Benchmark]
    public VerdictKind Analyze() => CutoffAnalyzer.Analyze(_columns, SampleRate).Kind;
}
