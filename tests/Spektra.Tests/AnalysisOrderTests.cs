using Spektra.Core;

namespace Spektra.Tests;

public sealed class AnalysisOrderTests
{
    private static AuditTarget T(string name, long size) => new(name, size, MtimeTicks: 1);

    [Test]
    public async Task FolderOrder_keeps_the_given_sequence()
    {
        AuditTarget[] worklist = [T("b.flac", 30), T("a.flac", 10), T("c.flac", 20)];

        var ordered = FolderAudit.OrderWorklist(worklist, AnalysisOrder.FolderOrder);

        await Assert.That(ordered.SequenceEqual(worklist)).IsTrue();
    }

    [Test]
    public async Task SmallestFirst_sorts_ascending_by_size()
    {
        AuditTarget[] worklist = [T("b.flac", 30), T("a.flac", 10), T("c.flac", 20)];

        var ordered = FolderAudit.OrderWorklist(worklist, AnalysisOrder.SmallestFirst);

        await Assert.That(ordered.Select(t => t.Path).SequenceEqual(
            new[] { "a.flac", "c.flac", "b.flac" })).IsTrue();
    }

    [Test]
    public async Task LargestFirst_sorts_descending_by_size()
    {
        AuditTarget[] worklist = [T("b.flac", 30), T("a.flac", 10), T("c.flac", 20)];

        var ordered = FolderAudit.OrderWorklist(worklist, AnalysisOrder.LargestFirst);

        await Assert.That(ordered.Select(t => t.Path).SequenceEqual(
            new[] { "b.flac", "c.flac", "a.flac" })).IsTrue();
    }

    [Test]
    public async Task Size_ties_keep_the_given_order()
    {
        AuditTarget[] worklist = [T("z.flac", 10), T("a.flac", 10), T("m.flac", 10)];

        var ordered = FolderAudit.OrderWorklist(worklist, AnalysisOrder.SmallestFirst);

        await Assert.That(ordered.Select(t => t.Path).SequenceEqual(
            new[] { "z.flac", "a.flac", "m.flac" })).IsTrue();
    }
}
