using Spektra.Core;

namespace Spektra.Tests;

public class AudioDecoderTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static AudioDecoder Decoder() => new(FfmpegLocator.Locate([])!.FfmpegPath);

    [Test]
    public async Task Wav_DecodesExactSampleCount()
    {
        var total = Decoder()
            .DecodeMonoChunks(Path.Combine(Fixtures, "sine-1khz.wav"), CancellationToken.None)
            .Sum(c => (long)c.Length);
        await Assert.That(total).IsEqualTo(44100L * 3);
    }

    [Test]
    public async Task Flac_DecodesExactSampleCount_AndSaneAmplitude()
    {
        var chunks = Decoder()
            .DecodeMonoChunks(Path.Combine(Fixtures, "sine-1khz.flac"), CancellationToken.None)
            .ToList();
        await Assert.That(chunks.Sum(c => (long)c.Length)).IsEqualTo(44100L * 3);
        var peak = chunks.SelectMany(c => c).Max(MathF.Abs);
        await Assert.That(peak).IsBetween(0.85f, 0.95f); // fixture generated at volume 0.9
    }

    [Test]
    public async Task Mp3_DecodesApproximateSampleCount()
    {
        var total = Decoder()
            .DecodeMonoChunks(Path.Combine(Fixtures, "sine-1khz.mp3"), CancellationToken.None)
            .Sum(c => (long)c.Length);
        await Assert.That(total).IsBetween(44100L * 3 - 3000, 44100L * 3 + 3000); // mp3 encoder padding
    }

    [Test]
    public async Task Stereo_MixesDownToMonoSampleCount()
    {
        var total = Decoder()
            .DecodeMonoChunks(Path.Combine(Fixtures, "sine-1khz-stereo.wav"), CancellationToken.None)
            .Sum(c => (long)c.Length);
        await Assert.That(total).IsEqualTo(44100L * 3); // -ac 1: mono sample count, not 2x
    }

    [Test]
    public async Task PreCancelled_ThrowsWithoutSpawningFfmpeg()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.That(() => new AudioDecoder(@"C:\nope\ffmpeg.exe")
            .DecodeMonoChunks(@"C:\nope\file.wav", cts.Token)
            .ToList()).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task NonAudio_ThrowsWithStderr()
    {
        var ex = Assert.Throws<AudioDecodeException>(() =>
        {
            Decoder().DecodeMonoChunks(Path.Combine(Fixtures, "notaudio.txt"), CancellationToken.None).ToList();
        });
        await Assert.That(string.IsNullOrWhiteSpace(ex.StderrTail)).IsFalse();
    }

    private static int DominantBin(IEnumerable<float[]> chunks)
    {
        var engine = new SpectrogramEngine(new SpectrogramSettings());
        var col = engine.Columns(chunks, 0, CancellationToken.None).Skip(5).First();
        var p = 0;
        for (var k = 1; k < col.Length; k++) if (col[k] > col[p]) p = k;
        return p;
    }

    [Test]
    public async Task Segment_DecodesOnlyRequestedSpan()
    {
        var total = Decoder()
            .DecodeMonoChunks(Path.Combine(Fixtures, "sine-1khz.wav"), CancellationToken.None,
                new DecodeOptions(Start: TimeSpan.FromSeconds(1), Duration: TimeSpan.FromSeconds(1)))
            .Sum(c => (long)c.Length);
        await Assert.That(total).IsBetween(43900, 44300);
    }

    [Test]
    public async Task SampleRate_Override_ShiftsDominantBin()
    {
        // 1 kHz tone, window 2048. Native 44100 → bin ≈ 1000*2048/44100 ≈ 46.
        // Forced to 22050 → bin ≈ 1000*2048/22050 ≈ 93.
        var bin = DominantBin(Decoder().DecodeMonoChunks(
            Path.Combine(Fixtures, "sine-1khz.wav"), CancellationToken.None,
            new DecodeOptions(SampleRate: 22050)));
        await Assert.That(bin).IsBetween(92, 94);
    }

    [Test]
    public async Task Channel1_DecodesLeft1kHz()
    {
        var bin = DominantBin(Decoder().DecodeMonoChunks(
            Path.Combine(Fixtures, "sine-dual-channel.wav"), CancellationToken.None,
            new DecodeOptions(Channel: 0)));
        await Assert.That(bin).IsBetween(46, 47); // 1000 Hz / (44100 / 2048) ≈ 46.4
    }

    [Test]
    public async Task Channel2_DecodesRight3kHz()
    {
        var bin = DominantBin(Decoder().DecodeMonoChunks(
            Path.Combine(Fixtures, "sine-dual-channel.wav"), CancellationToken.None,
            new DecodeOptions(Channel: 1)));
        await Assert.That(bin).IsBetween(139, 140); // 3000 Hz ≈ bin 139.3
    }

    [Test]
    public async Task Mixdown_ContainsBothChannels()
    {
        var engine = new SpectrogramEngine(new SpectrogramSettings());
        var col = engine.Columns(
            Decoder().DecodeMonoChunks(
                Path.Combine(Fixtures, "sine-dual-channel.wav"), CancellationToken.None),
            0, CancellationToken.None).Skip(5).First();
        await Assert.That(col[46] > -12f).IsTrue();  // 1 kHz present
        await Assert.That(col[139] > -12f).IsTrue(); // 3 kHz present
    }
}
