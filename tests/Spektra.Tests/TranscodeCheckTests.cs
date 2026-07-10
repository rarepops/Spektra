using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public sealed class TranscodeCheckTests
{
    [Theory]
    [InlineData("flac")]
    [InlineData("alac")]
    [InlineData("ape")]
    [InlineData("wavpack")]
    [InlineData("tta")]
    [InlineData("wmalossless")]
    [InlineData("pcm_s16le")]
    [InlineData("pcm_f32le")]
    [InlineData("dsd_lsbf")]
    public void IsLosslessCodec_KnowsLosslessFamilies(string codec) =>
        Assert.True(TranscodeCheck.IsLosslessCodec(codec));

    [Theory]
    [InlineData("mp3")]
    [InlineData("aac")]
    [InlineData("vorbis")]
    [InlineData("opus")]
    [InlineData("wmav2")]
    [InlineData(null)]
    public void IsLosslessCodec_LossyOrUnknown_IsNot(string? codec) =>
        Assert.False(TranscodeCheck.IsLosslessCodec(codec));

    [Fact]
    public void LossyCutoffInLosslessContainer_IsSuspect() =>
        Assert.True(TranscodeCheck.IsSuspectLossy("flac", 900_000, 1, 16_800));

    [Fact]
    public void HonestMonoMp3_IsNotSuspect() =>
        // 64 kbps mono ~ 128 kbps stereo: a 16.8 kHz wall is exactly right.
        Assert.False(TranscodeCheck.IsSuspectLossy("mp3", 64_000, 1, 16_800));

    [Fact]
    public void HighBitrateMp3WithLowCutoff_IsSuspect() =>
        // 320 kbps stereo walling at 16 kHz was re-encoded from a ~128 source.
        Assert.True(TranscodeCheck.IsSuspectLossy("mp3", 320_000, 2, 16_000));

    [Fact]
    public void NormalStereoMp3_128_IsNotSuspect() =>
        Assert.False(TranscodeCheck.IsSuspectLossy("mp3", 128_000, 2, 15_800));

    [Fact]
    public void Vorbis_SkipsTheBitrateCheck() =>
        Assert.False(TranscodeCheck.IsSuspectLossy("vorbis", 320_000, 2, 12_000));

    [Theory]
    [InlineData(null, 320_000L, 2, 16_000.0)]   // no codec
    [InlineData("mp3", null, 2, 16_000.0)]      // no bitrate
    [InlineData("mp3", 320_000L, 0, 16_000.0)]  // unknown channels
    [InlineData("mp3", 320_000L, 2, null)]      // no cutoff
    public void MissingData_IsNeverSuspect(string? codec, long? bitrate, int? channels, double? cutoff) =>
        Assert.False(TranscodeCheck.IsSuspectLossy(codec, bitrate, channels, cutoff));
}
