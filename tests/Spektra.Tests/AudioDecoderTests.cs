using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class AudioDecoderTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static AudioDecoder Decoder() => new(FfmpegLocator.Locate([])!.FfmpegPath);

    [Fact]
    public void Wav_DecodesExactSampleCount()
    {
        var total = Decoder()
            .DecodeMonoChunks(Path.Combine(Fixtures, "sine-1khz.wav"), CancellationToken.None)
            .Sum(c => (long)c.Length);
        Assert.Equal(44100L * 3, total);
    }

    [Fact]
    public void Flac_DecodesExactSampleCount_AndSaneAmplitude()
    {
        var chunks = Decoder()
            .DecodeMonoChunks(Path.Combine(Fixtures, "sine-1khz.flac"), CancellationToken.None)
            .ToList();
        Assert.Equal(44100L * 3, chunks.Sum(c => (long)c.Length));
        var peak = chunks.SelectMany(c => c).Max(MathF.Abs);
        Assert.InRange(peak, 0.85f, 0.95f); // fixture generated at volume 0.9
    }

    [Fact]
    public void Mp3_DecodesApproximateSampleCount()
    {
        var total = Decoder()
            .DecodeMonoChunks(Path.Combine(Fixtures, "sine-1khz.mp3"), CancellationToken.None)
            .Sum(c => (long)c.Length);
        Assert.InRange(total, 44100L * 3 - 3000, 44100L * 3 + 3000); // mp3 encoder padding
    }

    [Fact]
    public void Stereo_MixesDownToMonoSampleCount()
    {
        var total = Decoder()
            .DecodeMonoChunks(Path.Combine(Fixtures, "sine-1khz-stereo.wav"), CancellationToken.None)
            .Sum(c => (long)c.Length);
        Assert.Equal(44100L * 3, total); // -ac 1: mono sample count, not 2x
    }

    [Fact]
    public void NonAudio_ThrowsWithStderr()
    {
        var ex = Assert.Throws<AudioDecodeException>(() => Decoder()
            .DecodeMonoChunks(Path.Combine(Fixtures, "notaudio.txt"), CancellationToken.None)
            .ToList());
        Assert.False(string.IsNullOrWhiteSpace(ex.StderrTail));
    }

    private static int DominantBin(IEnumerable<float[]> chunks)
    {
        var engine = new SpectrogramEngine(new SpectrogramSettings());
        var col = engine.Columns(chunks, 0, CancellationToken.None).Skip(5).First();
        var p = 0;
        for (var k = 1; k < col.Length; k++) if (col[k] > col[p]) p = k;
        return p;
    }

    [Fact]
    public void Segment_DecodesOnlyRequestedSpan()
    {
        var total = Decoder()
            .DecodeMonoChunks(Path.Combine(Fixtures, "sine-1khz.wav"), CancellationToken.None,
                new DecodeOptions(Start: TimeSpan.FromSeconds(1), Duration: TimeSpan.FromSeconds(1)))
            .Sum(c => (long)c.Length);
        Assert.InRange(total, 43900, 44300);
    }

    [Fact]
    public void Channel1_DecodesLeft1kHz()
    {
        var bin = DominantBin(Decoder().DecodeMonoChunks(
            Path.Combine(Fixtures, "sine-dual-channel.wav"), CancellationToken.None,
            new DecodeOptions(Channel: 0)));
        Assert.InRange(bin, 46, 47); // 1000 Hz / (44100 / 2048) ≈ 46.4
    }

    [Fact]
    public void Channel2_DecodesRight3kHz()
    {
        var bin = DominantBin(Decoder().DecodeMonoChunks(
            Path.Combine(Fixtures, "sine-dual-channel.wav"), CancellationToken.None,
            new DecodeOptions(Channel: 1)));
        Assert.InRange(bin, 139, 140); // 3000 Hz ≈ bin 139.3
    }

    [Fact]
    public void Mixdown_ContainsBothChannels()
    {
        var engine = new SpectrogramEngine(new SpectrogramSettings());
        var col = engine.Columns(
            Decoder().DecodeMonoChunks(
                Path.Combine(Fixtures, "sine-dual-channel.wav"), CancellationToken.None),
            0, CancellationToken.None).Skip(5).First();
        Assert.True(col[46] > -12f, $"1 kHz at {col[46]:0.0} dB");
        Assert.True(col[139] > -12f, $"3 kHz at {col[139]:0.0} dB");
    }
}
