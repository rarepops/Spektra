using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class FfprobeMetadataReaderTests
{
    private static readonly string Fixtures =
        Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static FfprobeMetadataReader Reader()
        => new(FfmpegLocator.Locate([])!.FfprobePath);

    [Fact]
    public void ReadsFlacMetadata()
    {
        var m = Reader().Read(Path.Combine(Fixtures, "sine-1khz.flac"));
        Assert.Equal("flac", m.Codec);
        Assert.Equal(44100, m.SampleRate);
        Assert.Equal(1, m.Channels);
        Assert.Equal(16, m.BitsPerSample);
        Assert.InRange(m.Duration.TotalSeconds, 2.9, 3.1);
    }

    [Fact]
    public void ReadsMp3Metadata_NoBitDepth_HasBitrate()
    {
        var m = Reader().Read(Path.Combine(Fixtures, "sine-1khz.mp3"));
        Assert.Equal("mp3", m.Codec);
        Assert.Null(m.BitsPerSample);
        Assert.NotNull(m.BitRateBps);
        Assert.InRange(m.BitRateBps!.Value, 100_000, 160_000);
    }

    [Fact]
    public void NonAudioFile_Throws()
    {
        var ex = Assert.Throws<AudioDecodeException>(
            () => Reader().Read(Path.Combine(Fixtures, "notaudio.txt")));
        Assert.False(string.IsNullOrEmpty(ex.Message));
    }

    [Fact]
    public void Parse_MalformedJson_ReadsAsDecodeError()
    {
        var ex = Assert.Throws<AudioDecodeException>(
            () => FfprobeMetadataReader.Parse("garbage {", "boom from stderr"));
        Assert.Contains("unreadable", ex.Message);
    }

    [Fact]
    public void Parse_ValidPayload_MapsFields()
    {
        var json = """
            {
              "streams": [ { "codec_name": "flac", "sample_rate": "44100",
                             "channels": 2, "bits_per_raw_sample": "16" } ],
              "format": { "duration": "3.0", "bit_rate": "900000" }
            }
            """;
        var m = FfprobeMetadataReader.Parse(json, "");
        Assert.Equal("flac", m.Codec);
        Assert.Equal(44100, m.SampleRate);
        Assert.Equal(2, m.Channels);
        Assert.Equal(16, m.BitsPerSample);
        Assert.Equal(900_000L, m.BitRateBps);
        Assert.Equal(3.0, m.Duration.TotalSeconds, 3);
        Assert.False(m.DurationIsEstimated);
    }

    [Fact]
    public void DisplayLine_FormatsSegments()
    {
        var m = new AudioMetadata("flac", 44100, 2, 16, 1_017_000, TimeSpan.FromSeconds(252));
        Assert.Equal("song.flac — FLAC · 44.1 kHz · 16-bit · 2 ch · 4:12 · 1017 kbps",
            m.ToDisplayLine("song.flac"));

        var lossy = new AudioMetadata("mp3", 48000, 2, null, null, TimeSpan.FromSeconds(3661));
        Assert.Equal("x.mp3 — MP3 · 48 kHz · 2 ch · 1:01:01", lossy.ToDisplayLine("x.mp3"));
    }
}
