using Spektra.Core;

namespace Spektra.Tests;

public sealed class FolderStatusTests
{
    [Test]
    public async Task Rollup_counts_and_classifies()
    {
        var status = FolderTree.Rollup(
        [
            RowSeverity.Clean,
            RowSeverity.Suspect,
            RowSeverity.Problem,
            null,
        ]);

        await Assert.That(status.Total).IsEqualTo(4);
        await Assert.That(status.Analyzed).IsEqualTo(3);
        await Assert.That(status.Suspect).IsEqualTo(1);
        await Assert.That(status.Problem).IsEqualTo(1);
        await Assert.That(status.Partial).IsTrue();
        await Assert.That(status.Worst).IsEqualTo(RowSeverity.Problem);
    }

    [Test]
    public async Task Rollup_all_analyzed_is_not_partial()
    {
        var status = FolderTree.Rollup([RowSeverity.Clean, RowSeverity.Suspect]);

        await Assert.That(status.Partial).IsFalse();
        await Assert.That(status.Worst).IsEqualTo(RowSeverity.Suspect);
    }

    [Test]
    public async Task Rollup_none_analyzed_is_partial_and_clean()
    {
        var status = FolderTree.Rollup([null, null]);

        await Assert.That(status.Total).IsEqualTo(2);
        await Assert.That(status.Analyzed).IsEqualTo(0);
        await Assert.That(status.Partial).IsTrue();
        await Assert.That(status.Worst).IsEqualTo(RowSeverity.Clean);
    }

    [Test]
    public async Task Rollup_all_clean_is_analyzed_and_clean()
    {
        var status = FolderTree.Rollup([RowSeverity.Clean, RowSeverity.Clean]);

        await Assert.That(status.Total).IsEqualTo(2);
        await Assert.That(status.Analyzed).IsEqualTo(2);
        await Assert.That(status.Partial).IsFalse();
        await Assert.That(status.Worst).IsEqualTo(RowSeverity.Clean);
    }
}
