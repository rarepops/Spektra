using Spektra.Core;

namespace Spektra.Tests;

public class ProvenanceScanTests
{
    private const int Bins = 1025;
    private const double Nyquist = 22050.0; // 44.1 kHz / 2

    // One peak-hold column: -6 dB up to cutoffHz, floor above. cutoffHz >= Nyquist
    // is full-band. One column per second is used throughout (columns.Count ==
    // duration seconds), so a 30-second window is exactly 30 columns.
    private static float[] Column(double cutoffHz)
    {
        var hzPerBin = Nyquist / (Bins - 1);
        var col = new float[Bins];
        for (var k = 0; k < Bins; k++) col[k] = k * hzPerBin <= cutoffHz ? -6f : Db.Floor;
        return col;
    }

    private static float[] Silent()
    {
        var col = new float[Bins];
        Array.Fill(col, Db.Floor);
        return col;
    }

    private static IEnumerable<float[]> Run(double cutoffHz, int seconds) =>
        Enumerable.Range(0, seconds).Select(_ => Column(cutoffHz));

    private static AudioMetadata Flac(double seconds) =>
        new("flac", 44100, 2, 16, 900_000, TimeSpan.FromSeconds(seconds));

    private static AudioMetadata Mp3(double seconds, long bitrateBps) =>
        new("mp3", 44100, 2, null, bitrateBps, TimeSpan.FromSeconds(seconds));

    [Test]
    public async Task TranscodedRunInsideLosslessFile_IsMixed()
    {
        // 60 s full-band then 60 s walled at 16 kHz, in a FLAC. Whole-file
        // peak-hold reads Lossless (the full-band half fills the top bins); the
        // scan catches the two walled windows.
        List<float[]> columns = [.. Run(Nyquist, 60), .. Run(16_000, 60)];
        var v = ProvenanceScan.Analyze(columns, Flac(120));
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Mixed);
        await Assert.That(v.CutoffHz!.Value).IsBetween(15_000, 17_000);
        await Assert.That(v.Summary).Contains("1:00 to 2:00");
    }

    [Test]
    public async Task HomogeneousFullBand_IsLosslessUnchanged()
    {
        List<float[]> columns = [.. Run(Nyquist, 120)];
        var v = ProvenanceScan.Analyze(columns, Flac(120));
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Lossless);
        await Assert.That(v.CutoffHz).IsNull();
    }

    [Test]
    public async Task WholeFileAlreadyLossy_IsReturnedNotRelabeledMixed()
    {
        // Uniformly walled: the whole file already reads Lossy, and Mixed only
        // rescues a clean-ish read, so the guard returns it untouched.
        List<float[]> columns = [.. Run(16_000, 120)];
        var v = ProvenanceScan.Analyze(columns, Mp3(120, 128_000));
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Lossy);
    }

    [Test]
    public async Task SingleSuspectWindow_DoesNotTrigger()
    {
        // 90 s full-band, one 30 s walled window: below MinSuspectWindows (2),
        // so the file stays Lossless.
        List<float[]> columns = [.. Run(Nyquist, 90), .. Run(16_000, 30)];
        var v = ProvenanceScan.Analyze(columns, Flac(120));
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Lossless);
    }

    [Test]
    public async Task FileShorterThanTwoWindows_IsNeverScanned()
    {
        // 40 s total is below 2 * WindowSeconds, so the whole-file verdict stands.
        List<float[]> columns = [.. Run(Nyquist, 20), .. Run(16_000, 20)];
        var v = ProvenanceScan.Analyze(columns, Flac(40));
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Lossless);
    }

    [Test]
    public async Task SilentWindowBreaksAnOtherwiseTriggeringRun()
    {
        // full, walled, silent, walled. Without the silent window the last two
        // would be a walled run; the silent (Unknown) window between them keeps
        // the run under two. The full-band lead makes the whole file Lossless so
        // the scan actually runs.
        List<float[]> columns =
        [
            .. Run(Nyquist, 30), .. Run(16_000, 30),
            .. Enumerable.Range(0, 30).Select(_ => Silent()), .. Run(16_000, 30),
        ];
        var v = ProvenanceScan.Analyze(columns, Flac(120));
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Lossless);
    }

    [Test]
    public async Task WalledTail_WithColumnCountNotAMultipleOfTheWindow_StillTriggers()
    {
        // The decoder does not hand out one column per second, so the column count
        // is rarely a whole number of windows. 103 columns over 120 s makes the
        // window 26 columns wide, and 103 / 26 is 3.96: flooring that to 3 windows
        // swallowed the entire 60 s walled tail into one window, one short of the
        // two-window trigger, so a real compilation read clean. Caught by running
        // the built CLI on a synthesized compilation, not by the even-count tests.
        List<float[]> columns =
        [
            .. Enumerable.Range(0, 52).Select(_ => Column(Nyquist)),
            .. Enumerable.Range(0, 51).Select(_ => Column(16_000)),
        ];
        var v = ProvenanceScan.Analyze(columns, Flac(120));
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Mixed);
    }

    [Test]
    public async Task HonestLossyWindowsInAnMp3_AreNotMixed()
    {
        // Same shape as the Mixed case, but the container is a 128 kbps MP3: the
        // 16 kHz windows are honest for that bitrate, so IsSuspectLossy clears
        // them and the whole-Lossless read stands. Its FLAC counterpart trips,
        // because a lossless container should never carry that wall.
        List<float[]> columns = [.. Run(Nyquist, 60), .. Run(16_000, 60)];
        var v = ProvenanceScan.Analyze(columns, Mp3(120, 128_000));
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Lossless);
    }
}
