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
}
