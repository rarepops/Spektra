using Spektra.Core;

namespace Spektra.Tests;

public class DynamicsTests
{
    [Test]
    public async Task Sine_CrestIsAboutThreeDb()
    {
        var s = new float[44100];
        for (var i = 0; i < s.Length; i++) s[i] = 0.5f * MathF.Sin(2f * MathF.PI * 1000f * i / 44100f);
        var d = Dynamics.FromSamples([s]);
        await Assert.That(d.CrestFactorDb).IsBetween(2.5f, 3.5f);   // sine crest factor ~3.01 dB
        await Assert.That(d.PeakDbfs).IsBetween(-6.5f, -5.5f);      // 0.5 amplitude ~ -6 dBFS
        await Assert.That(d.ClippedSamples).IsEqualTo(0);
    }

    [Test]
    public async Task Clipping_IsCounted()
    {
        var d = Dynamics.FromSamples([[0.1f, 1.0f, -1.0f, 0.999f, 0.2f, 1.0f]]);
        await Assert.That(d.ClippedSamples).IsEqualTo(3);  // 1.0, -1.0, 1.0 (0.999 is below the 0.9997 threshold)
        await Assert.That(d.TotalSamples).IsEqualTo(6);
        await Assert.That(d.ClippedFraction).IsCloseTo(0.5, 0.001);
    }

    [Test]
    public async Task Empty_IsSafe()
    {
        var d = Dynamics.FromSamples([]);
        await Assert.That(d.TotalSamples).IsEqualTo(0);
        await Assert.That(d.ClippedFraction).IsCloseTo(0.0, 0.001);
    }
}

public class LoudnessMeasurerTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;

    private const string Summary = """
        [Parsed_ebur128_0 @ 000] t: 1.0  I:  -99.0 LUFS  LRA:  99.0 LU  TPK:  -99.0 dBFS
        [Parsed_ebur128_0 @ 000] Summary:
          Integrated loudness:
            I:          -3.9 LUFS
            Threshold: -13.9 LUFS
          Loudness range:
            LRA:        20.0 LU
            Threshold: -23.9 LUFS
            LRA low:   -23.9 LUFS
            LRA high:   -3.9 LUFS
          True peak:
            Peak:       -0.9 dBFS
        """;

    [Test]
    public async Task ParseEbur128_ReadsSummary_IgnoringProgressLines()
    {
        var (i, lra, tp) = LoudnessMeasurer.ParseEbur128(Summary);
        await Assert.That(i!.Value).IsCloseTo(-3.9, 0.001);
        await Assert.That(lra!.Value).IsCloseTo(20.0, 0.001);
        await Assert.That(tp!.Value).IsCloseTo(-0.9, 0.001);
    }

    [Test]
    public async Task ParseEbur128_MissingSummary_ReturnsNulls()
    {
        var (i, lra, tp) = LoudnessMeasurer.ParseEbur128("no summary here");
        await Assert.That(i).IsNull();
        await Assert.That(lra).IsNull();
        await Assert.That(tp).IsNull();
    }

    [Test]
    public async Task Measure_Sine_ReturnsLoudnessAndCrest()
    {
        var r = new LoudnessMeasurer(Ff).Measure(Path.Combine(Fixtures, "sine-1khz.wav"));
        await Assert.That(r.IntegratedLufs).IsNotNull();
        await Assert.That(r.TruePeakDbtp).IsNotNull();
        await Assert.That(r.TruePeakDbtp!.Value).IsBetween(-2.0, 0.0);
        await Assert.That(r.CrestFactorDb).IsBetween(2.5f, 3.5f);
    }

    [Test]
    public async Task Measure_PreCancelled_ThrowsWithoutSpawningFfmpeg()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var bogus = new FfmpegPaths(@"C:\nope\ffmpeg.exe", @"C:\nope\ffprobe.exe");
        await Assert.That(() => new LoudnessMeasurer(bogus).Measure(@"C:\nope\file.wav", ct: cts.Token))
            .Throws<OperationCanceledException>();
    }
}
