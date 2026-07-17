using Spektra.Core;

namespace Spektra.Tests;

public sealed class HtmlReportTests
{
    private static AuditRow Row(string file, string codec = "flac", string bandwidth = "Lossless",
        double? cutoffHz = null, string integrity = "Ok") =>
        new(file, codec, 44100, 2, 900_000, 60, bandwidth, cutoffHz, integrity, 0, 0, false, null);

    private static DupesResult OneGroup(string pathA, string pathB) =>
        new(
            [new DupesGroupReport(
                new DuplicateGroup(1, "song label", "High",
                    [new DuplicateMember(pathA, 1.0, "High", false),
                     new DuplicateMember(pathB, 0.9, "High", true)]),
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
        var html = HtmlReport.DupesDocument(OneGroup(@"C:\m\<script>alert(1)</script>.flac", @"C:\m\b&c.mp3"), "T");
        await Assert.That(html).DoesNotContain("<script>alert");
        await Assert.That(html).Contains("&lt;script&gt;");
        await Assert.That(html).Contains("b&amp;c.mp3");
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
}
