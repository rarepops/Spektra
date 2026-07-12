using Spektra.Core;

namespace Spektra.Tests;

public class FfmpegVersionTests
{
    [Test]
    [Arguments("ffmpeg version 6.1.1 Copyright (c) 2000-2023 the FFmpeg developers", "6.1.1")]
    [Arguments("ffmpeg version 8.1.2-essentials_build-www.gyan.dev Copyright (c) 2000-2025", "8.1.2-essentials_build-www.gyan.dev")]
    [Arguments("ffmpeg version n7.1-11-g1a2b3c4d5e Copyright", "n7.1-11-g1a2b3c4d5e")]
    [Arguments("  ffmpeg version 5.0 Copyright", "5.0")]
    public async Task Parse_ExtractsToken(string line, string expected)
    {
        await Assert.That(FfmpegVersion.Parse(line)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("some unrelated banner")]
    [Arguments("ffprobe version 6.1.1 Copyright")]
    public async Task Parse_RejectsNonBanners(string? line)
    {
        await Assert.That(FfmpegVersion.Parse(line)).IsNull();
    }
}
