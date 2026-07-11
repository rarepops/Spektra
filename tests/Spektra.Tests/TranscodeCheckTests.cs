using Spektra.Core;

namespace Spektra.Tests;

public sealed class TranscodeCheckTests
{
    [Test]
    [Arguments("flac")]
    [Arguments("alac")]
    [Arguments("ape")]
    [Arguments("wavpack")]
    [Arguments("tta")]
    [Arguments("wmalossless")]
    [Arguments("pcm_s16le")]
    [Arguments("pcm_f32le")]
    [Arguments("dsd_lsbf")]
    public async Task IsLosslessCodec_KnowsLosslessFamilies(string codec) =>
        await Assert.That(TranscodeCheck.IsLosslessCodec(codec)).IsTrue();

    [Test]
    [Arguments("mp3")]
    [Arguments("aac")]
    [Arguments("vorbis")]
    [Arguments("opus")]
    [Arguments("wmav2")]
    [Arguments(null)]
    public async Task IsLosslessCodec_LossyOrUnknown_IsNot(string? codec) =>
        await Assert.That(TranscodeCheck.IsLosslessCodec(codec)).IsFalse();

    [Test]
    public async Task LossyCutoffInLosslessContainer_IsSuspect() =>
        await Assert.That(TranscodeCheck.IsSuspectLossy("flac", 900_000, 1, 16_800)).IsTrue();

    [Test]
    public async Task HonestMonoMp3_IsNotSuspect() =>
        // 64 kbps mono ~ 128 kbps stereo: a 16.8 kHz wall is exactly right.
        await Assert.That(TranscodeCheck.IsSuspectLossy("mp3", 64_000, 1, 16_800)).IsFalse();

    [Test]
    public async Task HighBitrateMp3WithLowCutoff_IsSuspect() =>
        // 320 kbps stereo walling at 16 kHz was re-encoded from a ~128 source.
        await Assert.That(TranscodeCheck.IsSuspectLossy("mp3", 320_000, 2, 16_000)).IsTrue();

    [Test]
    public async Task NormalStereoMp3_128_IsNotSuspect() =>
        await Assert.That(TranscodeCheck.IsSuspectLossy("mp3", 128_000, 2, 15_800)).IsFalse();

    [Test]
    public async Task Vorbis_SkipsTheBitrateCheck() =>
        await Assert.That(TranscodeCheck.IsSuspectLossy("vorbis", 320_000, 2, 12_000)).IsFalse();

    [Test]
    [Arguments(null, 320_000L, 2, 16_000.0)]   // no codec
    [Arguments("mp3", null, 2, 16_000.0)]      // no bitrate
    [Arguments("mp3", 320_000L, 0, 16_000.0)]  // unknown channels
    [Arguments("mp3", 320_000L, 2, null)]      // no cutoff
    public async Task MissingData_IsNeverSuspect(string? codec, long? bitrate, int? channels, double? cutoff) =>
        await Assert.That(TranscodeCheck.IsSuspectLossy(codec, bitrate, channels, cutoff)).IsFalse();
}
