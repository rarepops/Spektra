using Spektra.Core;

namespace Spektra.Tests;

public class FfprobeMetadataReaderTests
{
    private static readonly string Fixtures =
        Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static FfprobeMetadataReader Reader()
        => new(FfmpegLocator.Locate([])!.FfprobePath);

    [Test]
    public async Task ReadsFlacMetadata()
    {
        var m = Reader().Read(Path.Combine(Fixtures, "sine-1khz.flac"));
        await Assert.That(m.Codec).IsEqualTo("flac");
        await Assert.That(m.SampleRate).IsEqualTo(44100);
        await Assert.That(m.Channels).IsEqualTo(1);
        await Assert.That(m.BitsPerSample).IsEqualTo(16);
        await Assert.That(m.Duration.TotalSeconds).IsBetween(2.9, 3.1);
    }

    [Test]
    public async Task ReadsMp3Metadata_NoBitDepth_HasBitrate()
    {
        var m = Reader().Read(Path.Combine(Fixtures, "sine-1khz.mp3"));
        await Assert.That(m.Codec).IsEqualTo("mp3");
        await Assert.That(m.BitsPerSample).IsNull();
        await Assert.That(m.BitRateBps).IsNotNull();
        await Assert.That(m.BitRateBps!.Value).IsBetween(100_000, 160_000);
    }

    [Test]
    public async Task NonAudioFile_Throws()
    {
        var ex = Assert.Throws<AudioDecodeException>(
            () => { Reader().Read(Path.Combine(Fixtures, "notaudio.txt")); });
        await Assert.That(string.IsNullOrEmpty(ex.Message)).IsFalse();
    }

    [Test]
    public async Task Parse_MalformedJson_ReadsAsDecodeError()
    {
        var ex = Assert.Throws<AudioDecodeException>(
            () => { FfprobeMetadataReader.Parse("garbage {", "boom from stderr"); });
        await Assert.That(ex.Message).Contains("unreadable");
    }

    [Test]
    public async Task Parse_ValidPayload_MapsFields()
    {
        var json = """
            {
              "streams": [ { "codec_name": "flac", "sample_rate": "44100",
                             "channels": 2, "bits_per_raw_sample": "16" } ],
              "format": { "duration": "3.0", "bit_rate": "900000" }
            }
            """;
        var m = FfprobeMetadataReader.Parse(json, "");
        await Assert.That(m.Codec).IsEqualTo("flac");
        await Assert.That(m.SampleRate).IsEqualTo(44100);
        await Assert.That(m.Channels).IsEqualTo(2);
        await Assert.That(m.BitsPerSample).IsEqualTo(16);
        await Assert.That(m.BitRateBps).IsEqualTo(900_000L);
        await Assert.That(m.Duration.TotalSeconds).IsCloseTo(3.0, 0.001);
        await Assert.That(m.DurationIsEstimated).IsFalse();
    }

    [Test]
    public async Task DisplayLine_FormatsSegments()
    {
        var m = new AudioMetadata("flac", 44100, 2, 16, 1_017_000, TimeSpan.FromSeconds(252));
        await Assert.That(m.ToDisplayLine("song.flac"))
            .IsEqualTo("song.flac — FLAC · 44.1 kHz · 16-bit · 2 ch · 4:12 · 1017 kbps");

        var lossy = new AudioMetadata("mp3", 48000, 2, null, null, TimeSpan.FromSeconds(3661));
        await Assert.That(lossy.ToDisplayLine("x.mp3")).IsEqualTo("x.mp3 — MP3 · 48 kHz · 2 ch · 1:01:01");
    }
}
