using Spektra.Core;

namespace Spektra.Tests;

public class IntegrityScannerTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly FfmpegPaths Ff = FfmpegLocator.Locate([])!;
    private static string P(string file) => Path.Combine(Fixtures, file);
    private static AudioMetadata Meta(string file) =>
        new AnalysisSession(Ff).ReadMetadata(P(file));

    [Test]
    public async Task Check_PreCancelled_ThrowsWithoutSpawningFfmpeg()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var bogus = new FfmpegPaths(@"C:\nope\ffmpeg.exe", @"C:\nope\ffprobe.exe");
        var meta = new AudioMetadata("flac", 44100, 1, 16, null, TimeSpan.FromSeconds(3));
        await Assert.That(() => new IntegrityScanner(bogus).Check(@"C:\nope\file.flac", meta, cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task CleanFile_IsOk()
    {
        var r = new IntegrityScanner(Ff).Check(P("chirp.wav"), Meta("chirp.wav"), CancellationToken.None);
        await Assert.That(r.Status).IsEqualTo(IntegrityStatus.Ok);
        await Assert.That(r.DecodeErrors).IsEqualTo(0);
        await Assert.That(r.Truncated).IsFalse();
        await Assert.That(r.Dropouts).IsEmpty();
    }

    [Test]
    public async Task TruncatedFile_IsCorrupt()
    {
        var meta = Meta("corrupt.flac"); // header still reports the full 3 s
        var r = new IntegrityScanner(Ff).Check(P("corrupt.flac"), meta, CancellationToken.None);
        await Assert.That(r.Status).IsEqualTo(IntegrityStatus.Corrupt);
        await Assert.That(r.Truncated || r.DecodeErrors > 0).IsTrue();
        await Assert.That(r.DecodedSeconds < r.ExpectedSeconds).IsTrue();
    }

    [Test]
    public async Task JunkAppended_EstimatedDuration_IsSuspectNotTruncated()
    {
        // 5 s of healthy audio + 100 KB of junk: the header duration is a
        // size-based estimate (~11 s), and the junk yields a stray resync
        // error. Playable file: worth a listen, not corrupt, not truncated.
        var meta = Meta("junk-appended.mp3");
        await Assert.That(meta.DurationIsEstimated).IsTrue();

        var r = new IntegrityScanner(Ff).Check(P("junk-appended.mp3"), meta, CancellationToken.None);

        await Assert.That(r.Truncated).IsFalse();
        await Assert.That(r.Status).IsEqualTo(IntegrityStatus.Suspect);
        await Assert.That(r.DecodeErrors).IsBetween(1, 2);
    }

    [Test]
    public async Task AuthoritativeHeaders_AreNotEstimated()
    {
        await Assert.That(Meta("chirp.wav").DurationIsEstimated).IsFalse();
        await Assert.That(Meta("sine-1khz.flac").DurationIsEstimated).IsFalse();
    }

    [Test]
    public async Task SilentGap_IsInformational_NotSuspect()
    {
        // Interior digital silence is reported (summary, row, lane) but is
        // legal audio and cannot be proven to be damage, so the status stays Ok.
        var r = new IntegrityScanner(Ff).Check(P("gap.wav"), Meta("gap.wav"), CancellationToken.None);

        await Assert.That(r.Status).IsEqualTo(IntegrityStatus.Ok);
        await Assert.That(r.Dropouts).HasSingleItem();
        var gap = r.Dropouts.Single();
        await Assert.That(gap.StartSeconds).IsBetween(0.8, 1.2);
        await Assert.That(r.Summary).Contains("silent gap");
    }
}
