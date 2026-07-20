using Spektra.Core;

namespace Spektra.Tests;

public sealed class WorklistSelectionTests
{
    private static WorklistCandidate C(string name, bool analyzed) =>
        new(new AuditTarget(name, SizeBytes: 10, MtimeTicks: 1), analyzed);

    [Test]
    public async Task Unanalyzed_files_are_always_selected()
    {
        WorklistCandidate[] candidates = [C("a.flac", analyzed: false)];

        var stale = FolderAudit.SelectWorklist(candidates, fresh: false);
        var forced = FolderAudit.SelectWorklist(candidates, fresh: true);

        await Assert.That(stale.Select(t => t.Path).SequenceEqual(["a.flac"])).IsTrue();
        await Assert.That(forced.Select(t => t.Path).SequenceEqual(["a.flac"])).IsTrue();
    }

    [Test]
    public async Task Analyzed_files_are_skipped_unless_fresh()
    {
        WorklistCandidate[] candidates = [C("a.flac", analyzed: true)];

        var stale = FolderAudit.SelectWorklist(candidates, fresh: false);
        var forced = FolderAudit.SelectWorklist(candidates, fresh: true);

        await Assert.That(stale).IsEmpty();
        await Assert.That(forced.Select(t => t.Path).SequenceEqual(["a.flac"])).IsTrue();
    }

    [Test]
    public async Task Mixed_set_selects_only_the_unanalyzed_when_not_fresh()
    {
        WorklistCandidate[] candidates =
            [C("a.flac", true), C("b.flac", false), C("c.flac", true)];

        var selected = FolderAudit.SelectWorklist(candidates, fresh: false);

        await Assert.That(selected.Select(t => t.Path).SequenceEqual(["b.flac"])).IsTrue();
    }

    [Test]
    public async Task Input_order_is_preserved_for_the_OrderWorklist_handoff()
    {
        WorklistCandidate[] candidates =
            [C("b.flac", false), C("a.flac", false), C("c.flac", false)];

        var selected = FolderAudit.SelectWorklist(candidates, fresh: false);

        await Assert.That(selected.Select(t => t.Path)
            .SequenceEqual(["b.flac", "a.flac", "c.flac"])).IsTrue();
    }

    [Test]
    public async Task Empty_candidate_set_yields_an_empty_worklist()
    {
        var selected = FolderAudit.SelectWorklist([], fresh: true);

        await Assert.That(selected).IsEmpty();
    }
}
