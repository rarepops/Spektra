using Spektra.Core;
using static Spektra.Tests.TestAudio;

namespace Spektra.Tests;

/// Labels pairs the way Duplicate Detective's "Same name, different audio"
/// section does, by how the first 10 s of the two copies line up. The files are
/// built once for the class from the committed fixtures, in a temp folder the
/// class deletes. Every fixture is 3 s long, so each song below is a fixture
/// looped, without re-encoding.
public sealed class SuspectLabelerTests
{
    private static string _dir = "";
    private static string P(string name) => Path.Combine(_dir, name);

    [Before(Class)]
    public static void BuildAudio()
    {
        _dir = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "spektra-suspect-labeler-" + Guid.NewGuid().ToString("N"))).FullName;

        // 12 s of white noise. Its 3 s period puts every repeat of the correlation
        // peak outside the 2 s search, so a copy lines up at one lag only.
        Loop("noise.wav", 4, P("base.wav"));
        File.Copy(P("base.wav"), P("same.wav"));
        // The same recording at half the level and 300 ms late.
        Ffmpeg("-i", P("base.wav"), "-af", "volume=0.5,adelay=300", P("later.wav"));
        // Half of base and half unrelated material (its own reverse) at equal
        // level, which reads about 0.71.
        Ffmpeg("-i", P("base.wav"), "-filter_complex",
            "[0:a]asplit[x][y];[y]areverse[r];[x][r]amix=inputs=2:normalize=0,volume=0.5", P("mixed.wav"));
        Loop("chirp.wav", 4, P("other.wav"));
        // 3 s, under the 10 s window.
        File.Copy(Fixture("noise.wav"), P("stub.wav"));
        // 15 s is under the 20 s fingerprint minimum but over the window, so the
        // pair is still aligned.
        Loop("noise.wav", 5, P("fifteen.wav"));
        Ffmpeg("-i", P("fifteen.wav"), "-af", "volume=0.8", P("fifteen-quiet.wav"));
        File.Copy(Fixture("notaudio.txt"), P("broken.wav"));
    }

    [After(Class)]
    public static void DeleteAudio() => Directory.Delete(_dir, recursive: true);

    [Test]
    public async Task Label_IdenticalCopies_AreTheSamePerformance()
    {
        var verdict = SuspectLabeler.Label(Ff, P("base.wav"), P("same.wav"));

        await Assert.That(verdict.Label).IsEqualTo(SuspectLabel.SamePerformance);
        await Assert.That(verdict.Confidence).IsNotNull();
        await Assert.That(verdict.Confidence!.Value).IsGreaterThanOrEqualTo(0.99);
    }

    [Test]
    public async Task Label_AQuieterLaterCopy_IsTheSamePerformance()
    {
        var verdict = SuspectLabeler.Label(Ff, P("base.wav"), P("later.wav"));

        await Assert.That(verdict.Label).IsEqualTo(SuspectLabel.SamePerformance);
    }

    [Test]
    public async Task Label_AMixWithOtherMaterial_IsPartlyTheSame()
    {
        var verdict = SuspectLabeler.Label(Ff, P("base.wav"), P("mixed.wav"));

        await Assert.That(verdict.Label).IsEqualTo(SuspectLabel.PartlySame);
    }

    [Test]
    public async Task Label_ADifferentSound_DoesNotLineUp()
    {
        var verdict = SuspectLabeler.Label(Ff, P("base.wav"), P("other.wav"));

        await Assert.That(verdict.Label).IsEqualTo(SuspectLabel.DoesntLineUp);
        await Assert.That(verdict.Confidence).IsNotNull();
        await Assert.That(verdict.Confidence!.Value).IsLessThan(0.2);
    }

    [Test]
    public async Task Label_ACopyShorterThanTheWindow_CannotBeCompared()
    {
        var verdict = SuspectLabeler.Label(Ff, P("base.wav"), P("stub.wav"));

        await Assert.That(verdict.Label).IsEqualTo(SuspectLabel.CantCompare);
        await Assert.That(verdict.Confidence).IsNull();
    }

    [Test]
    public async Task Label_AFileThatDoesNotDecode_CannotBeCompared()
    {
        var verdict = SuspectLabeler.Label(Ff, P("base.wav"), P("broken.wav"));

        await Assert.That(verdict.Label).IsEqualTo(SuspectLabel.CantCompare);
    }

    [Test]
    public async Task Label_AFileThatIsGone_CannotBeCompared()
    {
        // Deleted, renamed or on an unplugged drive between the scan and the label.
        var verdict = SuspectLabeler.Label(Ff, P("base.wav"), P("never-written.wav"));

        await Assert.That(verdict.Label).IsEqualTo(SuspectLabel.CantCompare);
    }

    [Test]
    public async Task Label_AFifteenSecondPair_IsCompared_NotRefused()
    {
        var verdict = SuspectLabeler.Label(Ff, P("fifteen.wav"), P("fifteen-quiet.wav"));

        await Assert.That(verdict.Label).IsEqualTo(SuspectLabel.SamePerformance);
    }

    [Test]
    public async Task Label_ACancelledToken_Throws_NotCantCompare()
    {
        // A cancel is "not checked", never a copy that "can't be compared": the
        // view model keeps any verdict that arrived.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(() => SuspectLabeler.Label(Ff, P("base.wav"), P("same.wav"), cts.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task FromConfidence_PinsTheBandEdges()
    {
        await Assert.That(SuspectLabeler.FromConfidence(0)).IsEqualTo(SuspectLabel.DoesntLineUp);
        await Assert.That(SuspectLabeler.FromConfidence(0.4499)).IsEqualTo(SuspectLabel.DoesntLineUp);
        await Assert.That(SuspectLabeler.FromConfidence(0.45)).IsEqualTo(SuspectLabel.PartlySame);
        await Assert.That(SuspectLabeler.FromConfidence(0.7999)).IsEqualTo(SuspectLabel.PartlySame);
        await Assert.That(SuspectLabeler.FromConfidence(0.80)).IsEqualTo(SuspectLabel.SamePerformance);
        await Assert.That(SuspectLabeler.FromConfidence(1)).IsEqualTo(SuspectLabel.SamePerformance);
    }

    [Test]
    public async Task LabelAll_ReturnsVerdictsInPairOrder_AndReportsEachOnce()
    {
        var recorder = new Recorder();

        var verdicts = SuspectLabeler.LabelAll(Ff, Pairs(), jobs: 3, recorder);

        await Assert.That(string.Join(", ", verdicts.Select(v => v.Label)))
            .IsEqualTo("SamePerformance, CantCompare, DoesntLineUp");
        var reports = recorder.Reports;
        await Assert.That(string.Join(", ", reports.Select(r => r.Index).Order())).IsEqualTo("0, 1, 2");
        await Assert.That(reports[^1].Done).IsEqualTo(3);
    }

    [Test]
    public async Task LabelAll_ACancelledToken_Throws_AndLabelsNothing()
    {
        var recorder = new Recorder();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.That(() => SuspectLabeler.LabelAll(Ff, Pairs(), jobs: 3, recorder, cts.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(recorder.Reports).IsEmpty();
    }

    /// Identical copies, a copy too short to compare, and another sound, in that order.
    private static SuspectPair[] Pairs() =>
        [Pair("base.wav", "same.wav"), Pair("base.wav", "stub.wav"), Pair("base.wav", "other.wav")];

    private static SuspectPair Pair(string a, string b) =>
        new(P(a), "/music/Old", 12, null, P(b), "/music/New", 12, null, NameStrength.SameName);

    /// Appends each report under a lock as it arrives. Progress<T> would post them
    /// and deliver them after the call returns.
    private sealed class Recorder : IProgress<LabelProgress>
    {
        private readonly List<LabelProgress> _reports = [];

        public void Report(LabelProgress value)
        {
            lock (_reports) _reports.Add(value);
        }

        public LabelProgress[] Reports
        {
            get { lock (_reports) return [.. _reports]; }
        }
    }
}
