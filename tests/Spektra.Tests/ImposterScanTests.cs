using Spektra.Core;
using static Spektra.Tests.TestAudio;

namespace Spektra.Tests;

/// Runs the Imposter Buster scan over ImposterLibrary once for the class: the duplicate scan over the
/// reference folder and the folders to check, the alignment of the pairs no identical fingerprint
/// settles, and the rule over both. ImposterSongsTests pins the rule; this pins the wiring around it.
public sealed class ImposterScanTests
{
    private static ImposterLibrary _lib = null!;
    private static ImposterResult _result = null!;
    private static readonly Collector Reports = new();

    [Before(Class)]
    public static void ScanLibrary()
    {
        _lib = new ImposterLibrary("spektra-imposter-scan-");
        _result = ImposterScan.Run(Ff, _lib.Rips, [_lib.Old, _lib.New], jobs: 2, progress: Reports);
    }

    [After(Class)]
    public static void DeleteLibrary() => _lib.Dispose();

    // A path as these tests name a file: its folder, then its name.
    private static string Short(string path) => $"{Path.GetFileName(Path.GetDirectoryName(path))}/{Path.GetFileName(path)}";

    private static string Names(IEnumerable<ScannedFile> files) => string.Join(", ", files.Select(f => Short(f.Path)));

    // A song as "verdict: copy grade, copy grade".
    private static string Show(ImposterSong song) =>
        $"{song.Verdict}: " + string.Join(", ", song.Copies.Select(c => $"{Short(c.File.Path)} {c.Grade}"));

    [Test]
    public async Task Run_GradesEachCopyAgainstItsReferenceFile()
    {
        await Assert.That(string.Join(", ", _result.Songs.Select(s => Short(s.Reference.Path)))).IsEqualTo(
            "Rips/Artist Name - Song Five.wav, Rips/Artist Name - Song Four.wav, Rips/Artist Name - Song One.mp3, "
            + "Rips/Artist Name - Song Three.wav, Rips/Artist Name - Song Two.wav");
        var (five, four, one, three, two) = (_result.Songs[0], _result.Songs[1], _result.Songs[2], _result.Songs[3], _result.Songs[4]);

        // The reference file is a lossy encode of Old's copy: the scan matches the two, but short of
        // all but identical, so the alignment confirms the copy. New's copy is another sound under
        // the same name.
        await Assert.That(Show(one)).IsEqualTo(
            "SomeConfirmed: Old/Artist Name - Song One.wav Confirmed, New/Artist Name - Song One.wav DoesntLineUp");
        await Assert.That(one.Copies[0].Similarity).IsGreaterThan(0);
        await Assert.That(one.Copies[0].Similarity).IsLessThan(ImposterSongs.IdenticalFrom);
        await Assert.That(one.Copies[0].AlignConfidence).IsNotNull();
        await Assert.That(one.Copies[0].AlignConfidence!.Value).IsGreaterThanOrEqualTo(SuspectLabeler.SamePerformanceFrom);
        await Assert.That(one.Copies[1].AlignConfidence).IsNotNull();
        await Assert.That(one.Copies[1].AlignConfidence!.Value).IsLessThan(SuspectLabeler.LinesUpFrom);
        await Assert.That(one.Best).IsNotNull();
        await Assert.That(one.Best!.Winners).DoesNotContain(one.Copies[1].File.Path);

        // Too short to fingerprint, so only the alignment can confirm the quieter copy.
        await Assert.That(Show(two)).IsEqualTo("AllConfirmed: Old/Artist Name - Song Two.wav Confirmed");
        await Assert.That(two.Copies[0].AlignConfidence).IsNotNull();
        await Assert.That(two.Copies[0].AlignConfidence!.Value).IsGreaterThanOrEqualTo(SuspectLabeler.SamePerformanceFrom);

        await Assert.That(Show(three)).IsEqualTo("NoneConfirmed: Old/Artist Name - Song Three.wav OtherLength");

        // A copy that cannot be decoded, which is also what a file deleted or unplugged after the scan
        // reads as, cannot be compared, and every copy above still has its grade.
        await Assert.That(Show(four)).IsEqualTo("NoneConfirmed: New/Artist Name - Song Four.wav CantCompare");
        await Assert.That(four.Copies[0].AlignConfidence).IsNull();
        await Assert.That(four.Copies[0].File.Row).IsNull();

        // New's copy is the reference file byte for byte, confirmed by its fingerprint with no
        // alignment. Old's opens with 5 s of silence, further than the alignment reaches: the
        // fingerprints still match, but the openings line up only in part, so it is the same by
        // fingerprint, and its note names New's copy, never the reference file it joined.
        await Assert.That(Show(five)).IsEqualTo(
            "SomeConfirmed: Old/Artist Name - Song Five.wav SameByFingerprint, New/Artist Name - Song Five.wav Confirmed");
        await Assert.That(five.Copies[1].AlignConfidence).IsNull();
        await Assert.That(five.Copies[1].Similarity).IsGreaterThanOrEqualTo(ImposterSongs.IdenticalFrom);
        await Assert.That(five.Copies[0].AlignConfidence).IsNotNull();
        await Assert.That(five.Copies[0].SameAudioAs).IsEqualTo(five.Copies[1].File.Path);
    }

    [Test]
    public async Task Run_ListsWhatHasNoCounterpart()
    {
        await Assert.That(Names(_result.NoReference)).IsEqualTo("Old/Bonus Track.wav");
        await Assert.That(Names(_result.ReferenceOnly)).IsEqualTo("Rips/Rips Only.wav");
        await Assert.That(_result.ReferenceRoot).IsEqualTo(Path.GetFullPath(_lib.Rips));
        await Assert.That(string.Join(" | ", _result.Roots))
            .IsEqualTo($"{Path.GetFullPath(_lib.Old)} | {Path.GetFullPath(_lib.New)}");
    }

    [Test]
    public async Task Run_ReportsTheAlignmentAsItsLastPhase()
    {
        var reports = Reports.All;

        // The phases in the order they ran, each run of reports collapsed to one name: an Analyzing
        // report after the first Checking one, or any other report after the alignment, adds a name.
        var phases = reports.Where((r, i) => i == 0 || r.Phase != reports[i - 1].Phase).Select(r => r.Phase);
        await Assert.That(string.Join(", ", phases)).IsEqualTo("Analyzing, Matching, Checking");
        var checking = reports.Where(r => r.Phase == "Checking").ToArray();
        await Assert.That(string.Join(", ", checking.Select(r => $"{r.Done}/{r.Total}"))).IsEqualTo("1/6, 2/6, 3/6, 4/6, 5/6, 6/6");
        await Assert.That(checking[^1].Fraction).IsEqualTo(1.0);
    }

    [Test]
    public async Task Run_ACancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(() => ImposterScan.Run(Ff, _lib.Rips, [_lib.Old, _lib.New], jobs: 2, ct: cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Run_ACancelDuringTheAlignment_Throws()
    {
        // Cancelled by the first alignment's report, once the duplicate scan is over, so only the
        // alignment is left to see it: a cancel there must not return a result with copies unchecked.
        using var cts = new CancellationTokenSource();
        var cancelAtChecking = new CancelAt("Checking", cts);

        await Assert.That(() => ImposterScan.Run(
                Ff, _lib.Rips, [_lib.Old, _lib.New], jobs: 2, progress: cancelAtChecking, ct: cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Run_TheReferenceAmongTheFoldersToCheck_IsRefused()
    {
        await Assert.That(() => ImposterScan.Run(
                Ff, _lib.Rips, [_lib.Old, _lib.Rips.ToUpperInvariant() + Path.DirectorySeparatorChar], jobs: 2))
            .Throws<ArgumentException>();
        await Assert.That(() => ImposterScan.Run(Ff, _lib.Rips, [], jobs: 2)).Throws<ArgumentException>();
    }

    [Test]
    public async Task IsSameFolder_IgnoresCaseAndATrailingSeparator()
    {
        await Assert.That(ImposterScan.IsSameFolder("/music/Rips", "/MUSIC/rips" + Path.DirectorySeparatorChar)).IsTrue();
        // By full path.
        await Assert.That(ImposterScan.IsSameFolder("/music/Rips", "/music/Old/../Rips")).IsTrue();
        // Never by a prefix: a folder whose name starts the same, or a folder inside it, is another folder.
        await Assert.That(ImposterScan.IsSameFolder("/music/Rips", "/music/Rips Two")).IsFalse();
        await Assert.That(ImposterScan.IsSameFolder("/music/Rips", "/music/Rips/Live")).IsFalse();
    }

    /// Appends each report under a lock as it arrives. Progress<T> would post them to the thread pool
    /// and could deliver them after Run returns.
    private sealed class Collector : IProgress<DupesProgress>
    {
        private readonly List<DupesProgress> _reports = [];

        public void Report(DupesProgress value)
        {
            lock (_reports) _reports.Add(value);
        }

        public DupesProgress[] All
        {
            get { lock (_reports) return [.. _reports]; }
        }
    }

    /// Cancels its token at the first report of a phase, on the thread that reports it.
    private sealed class CancelAt(string phase, CancellationTokenSource cts) : IProgress<DupesProgress>
    {
        public void Report(DupesProgress value)
        {
            if (value.Phase == phase) cts.Cancel();
        }
    }
}
