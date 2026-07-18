using Spektra.Core;

namespace Spektra.Tests;

public sealed class HtmlReportTests
{
    private static AuditRow Row(string file, string codec = "flac", string bandwidth = "Lossless",
        double? cutoffHz = null, string integrity = "Ok") =>
        new(file, codec, 44100, 2, 900_000, 60, bandwidth, cutoffHz, integrity, 0, 0, false, null);

    private static DupesResult OneGroup(string pathA, string pathB, string tier = "High") =>
        new(
            [new DupesGroupReport(
                new DuplicateGroup(1, "song label", tier,
                    [new DuplicateMember(pathA, 1.0, tier, false),
                     new DuplicateMember(pathB, 0.9, tier, true)]),
                new QualityRanking([pathA], "High", "winner is full-band lossless (flac), runner-up is mp3 cut at 16.0 kHz", [pathA, pathB]),
                new Dictionary<string, AuditRow> { [pathA] = Row(pathA), [pathB] = Row(pathB, "mp3", "Lossy", 16_000) },
                new Dictionary<string, long> { [pathA] = 30_000_000, [pathB] = 5_000_000 },
                5_000_000)],
            [], 2, 5_000_000);

    [Test]
    public async Task DupesDocument_IsSelfContained_AndMarksTheWinnerOnce()
    {
        var html = HtmlReport.DupesDocument(OneGroup(@"C:\m\a.flac", @"C:\m\b.mp3"), "Test report");
        await Assert.That(html).StartsWith("<!DOCTYPE html>");
        await Assert.That(html).Contains("song label");
        await Assert.That(html.Split('★').Length - 1).IsEqualTo(1); // one winner star
        await Assert.That(html).Contains("found by audio");
        await Assert.That(html).DoesNotContain("http://").Because("self-contained: no external requests");
        await Assert.That(html).DoesNotContain("https://");
    }

    [Test]
    public async Task DupesDocument_EscapesDataDerivedText()
    {
        var html = HtmlReport.DupesDocument(OneGroup(@"C:\m\<script>alert(1)</script>.flac", @"C:\m\b&c.mp3", "<High>"), "T");
        await Assert.That(html).DoesNotContain("<script>alert");
        await Assert.That(html).Contains("&lt;script&gt;");
        await Assert.That(html).Contains("b&amp;c.mp3");
        await Assert.That(html).DoesNotContain("<High>");
        await Assert.That(html).Contains("&lt;High&gt;");
    }

    [Test]
    public async Task DupesDocument_RendersTheNotAnalyzedSection_OnlyWhenPresent()
    {
        var with = new DupesResult([], [new NotAnalyzedFile(@"C:\m\tiny.wav", "shorter than 20 s")], 1, 0);
        var without = new DupesResult([], [], 1, 0);
        await Assert.That(HtmlReport.DupesDocument(with, "T")).Contains("not analyzed");
        await Assert.That(HtmlReport.DupesDocument(with, "T")).Contains("tiny.wav");
        await Assert.That(HtmlReport.DupesDocument(without, "T")).DoesNotContain("not analyzed");
    }

    [Test]
    public async Task AuditDocument_RendersEveryRow_WithSeverityClasses()
    {
        var rows = new List<AuditRow>
        {
            Row("clean.flac"),
            Row("transcode.flac", "flac", "Lossy", 16_000),
            Row("broken.mp3", "mp3", "Lossy", 16_000, integrity: "Corrupt"),
        };
        var html = HtmlReport.AuditDocument(rows, "Audit");
        await Assert.That(html).StartsWith("<!DOCTYPE html>");
        await Assert.That(html).Contains("clean.flac");
        await Assert.That(html.Split("data-sev=\"2\"").Length - 1).IsEqualTo(2); // transcode + corrupt
        await Assert.That(html).Contains("sortBy(this)");
    }

    [Test]
    public async Task AuditDocument_EscapesFileNames()
    {
        var html = HtmlReport.AuditDocument([Row("<img src=x>.flac")], "T");
        await Assert.That(html).DoesNotContain("<img src=x>");
        await Assert.That(html).Contains("&lt;img src=x&gt;");
    }

    private static PeekFolder PeekTree() =>
        new("<img src=x>", @"C:\lib",
            [new PeekFolder("disc1", @"C:\lib\disc1",
                [new PeekFolder("art", @"C:\lib\disc1\art", [],
                    [new PeekFile("front.jpg", @"C:\lib\disc1\art\front.jpg", "jpg", null, 100_000)],
                    "1 jpg", false)],
                [new PeekFile("<script>.mp3", @"C:\lib\disc1\<script>.mp3", "mp3", RowSeverity.Suspect, 5_000_000)],
                "1 mp3 · 1 jpg", false)],
            [new PeekFile("notes.nfo", @"C:\lib\notes.nfo", "nfo", null, 2_000)],
            "1 mp3 · 1 jpg · 1 nfo", false);

    [Test]
    public async Task PeekDocument_EscapesHostileNames_AndStaysSelfContained()
    {
        var html = HtmlReport.PeekDocument(PeekTree(), "Spektra Folder Peek");
        await Assert.That(html).DoesNotContain("<img src=x>");
        await Assert.That(html).Contains("&lt;img src=x&gt;");
        await Assert.That(html).DoesNotContain("<script>.mp3");
        await Assert.That(html).Contains("&lt;script&gt;.mp3");
        await Assert.That(html).DoesNotContain("http://");
        await Assert.That(html).DoesNotContain("https://");
    }

    [Test]
    public async Task PeekDocument_OpensTopLevels_DeeperStartClosed()
    {
        var html = HtmlReport.PeekDocument(PeekTree(), "t");
        // root (depth 0) and disc1 (depth 1) render open; art (depth 2) starts closed
        var open = html.Split("<details open>").Length - 1;
        var closed = html.Split("<details>").Length - 1;
        await Assert.That(open).IsEqualTo(2);
        await Assert.That(closed).IsEqualTo(1);
    }

    [Test]
    public async Task PeekDocument_ShowsRollupsAndSizes()
    {
        var html = HtmlReport.PeekDocument(PeekTree(), "t");
        await Assert.That(html).Contains("1 mp3 · 1 jpg · 1 nfo");
        await Assert.That(html).Contains("4.8 MB");
        await Assert.That(html).Contains("2.0 KB");
    }
}
