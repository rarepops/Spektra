using Spektra.Core;

namespace Spektra.Tests;

/// The folder tab's progress bar reads as coverage of the folder rather than
/// progress through one run, which is what lets it survive idle: open a folder
/// you already analyzed and the bar is full before you touch anything, and
/// clicking Analyze on it leaves the bar alone instead of doing nothing visible.
public class AnalysisCoverageTests
{
    [Test]
    public async Task Fraction_IsWhatIsKnownOverWhatIsThere()
    {
        await Assert.That(AnalysisCoverage.Fraction(0, 4)).IsEqualTo(0);
        await Assert.That(AnalysisCoverage.Fraction(1, 4)).IsEqualTo(0.25);
        await Assert.That(AnalysisCoverage.Fraction(4, 4)).IsEqualTo(1);
    }

    [Test]
    public async Task NothingThere_ReadsEmpty_NotFull()
    {
        // The deliberate opposite of ScanProgress.Fraction, which answers 1 at
        // zero bytes because a run with no work really is complete. A folder
        // holding no audio has not been analyzed, and a full bar would claim
        // knowledge that does not exist. FolderViewModel used to need an
        // IsAnalyzing guard purely to keep ScanProgress's answer off the screen
        // while idle; encoding the right answer here is what removes that need.
        await Assert.That(AnalysisCoverage.Fraction(0, 0)).IsEqualTo(0);
        await Assert.That(AnalysisCoverage.Fraction(3, 0)).IsEqualTo(0);
    }

    [Test]
    public async Task MoreKnownThanPresent_Clamps()
    {
        // Defensive: a refresh rebuilds the file list before the rows that
        // describe it are pruned, so for a moment the numerator can lead. A bar
        // past its own maximum is a rendering bug, not a story worth telling.
        await Assert.That(AnalysisCoverage.Fraction(7, 5)).IsEqualTo(1);
    }

    [Test]
    public async Task NegativeCounts_CannotDriveTheBarBackwards()
    {
        await Assert.That(AnalysisCoverage.Fraction(-2, 5)).IsEqualTo(0);
    }
}
