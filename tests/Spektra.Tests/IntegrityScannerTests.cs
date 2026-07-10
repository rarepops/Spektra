using Spektra.Core;
using Xunit;

namespace Spektra.Tests;

public class IntegrityScannerTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static string P(string file) => Path.Combine(Fixtures, file);
    private static AudioMetadata Meta(string file) =>
        new AnalysisSession(Ff).ReadMetadata(P(file));

    [Fact]
    public void CleanFile_IsOk()
    {
        var r = new IntegrityScanner(Ff).Check(P("chirp.wav"), Meta("chirp.wav"), CancellationToken.None);
        Assert.Equal(IntegrityStatus.Ok, r.Status);
        Assert.Equal(0, r.DecodeErrors);
        Assert.False(r.Truncated);
        Assert.Empty(r.Dropouts);
    }

    [Fact]
    public void TruncatedFile_IsCorrupt()
    {
        var meta = Meta("corrupt.flac"); // header still reports the full 3 s
        var r = new IntegrityScanner(Ff).Check(P("corrupt.flac"), meta, CancellationToken.None);
        Assert.Equal(IntegrityStatus.Corrupt, r.Status);
        Assert.True(r.Truncated || r.DecodeErrors > 0,
            $"expected truncation or decode errors (truncated={r.Truncated}, errors={r.DecodeErrors})");
        Assert.True(r.DecodedSeconds < r.ExpectedSeconds,
            $"decoded {r.DecodedSeconds}s should be short of {r.ExpectedSeconds}s");
    }

    [Fact]
    public void JunkAppended_EstimatedDuration_IsSuspectNotTruncated()
    {
        // 5 s of healthy audio + 100 KB of junk: the header duration is a
        // size-based estimate (~11 s), and the junk yields a stray resync
        // error. Playable file: worth a listen, not corrupt, not truncated.
        var meta = Meta("junk-appended.mp3");
        Assert.True(meta.DurationIsEstimated);

        var r = new IntegrityScanner(Ff).Check(P("junk-appended.mp3"), meta, CancellationToken.None);

        Assert.False(r.Truncated);
        Assert.Equal(IntegrityStatus.Suspect, r.Status);
        Assert.InRange(r.DecodeErrors, 1, 2);
    }

    [Fact]
    public void AuthoritativeHeaders_AreNotEstimated()
    {
        Assert.False(Meta("chirp.wav").DurationIsEstimated);
        Assert.False(Meta("sine-1khz.flac").DurationIsEstimated);
    }
}
