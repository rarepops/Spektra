using BenchmarkDotNet.Attributes;
using Spektra.Core;

namespace Spektra.Benchmarks;

/// SpectrogramEngine.Columns over ~5 s of synthetic mono at 44.1 kHz: the
/// dominant per-file pipeline. PeakHoldMerge toggles the two output paths.
[MemoryDiagnoser]
public class SpectrogramBenchmarks
{
    private const int SampleRate = 44100;
    private const int Seconds = 5;

    // Large enough that the engine's estimated window count exceeds the default
    // MaxColumns (4096), which flips it into the peak-hold merge branch on the
    // fixed 5 s signal. The engine reads only the chunks it is handed; this
    // estimate purely raises the merge factor.
    private const long MergeEstimatedSamples = 10_000_000;

    [Params(1024, 2048)]
    public int WindowSize { get; set; }

    [Params(false, true)]
    public bool PeakHoldMerge { get; set; }

    private float[][] _chunks = null!;
    private long _estimatedTotalSamples;

    [GlobalSetup]
    public void Setup()
    {
        var signal = Signal.Noise(SampleRate * Seconds, seed: 1234);
        _chunks = Signal.Chunk(signal, chunkSize: SampleRate / 10);
        _estimatedTotalSamples = PeakHoldMerge ? MergeEstimatedSamples : 0;
    }

    [Benchmark]
    public int Columns()
    {
        var engine = new SpectrogramEngine(new SpectrogramSettings(WindowSize: WindowSize));
        var count = 0;
        foreach (var _ in engine.Columns(_chunks, _estimatedTotalSamples, CancellationToken.None))
            count++;
        return count;
    }
}
