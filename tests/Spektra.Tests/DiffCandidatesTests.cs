using Spektra.Core;

namespace Spektra.Tests;

/// "Compare 'X' with" lists the other folders already open, so the second side
/// of a diff can be named without going back to disk. Which folders are worth
/// offering has rules, and the menu that renders them cannot be unit tested,
/// so the rules live here.
public class DiffCandidatesTests
{
    [Test]
    public async Task OffersEveryOtherOpenFolder()
    {
        var others = DiffCandidates.Other(
            @"D:\Music\A", [@"D:\Music\A", @"D:\Music\B", @"D:\Music\C"]);
        await Assert.That(others).IsEquivalentTo(new[] { @"D:\Music\B", @"D:\Music\C" });
    }

    [Test]
    public async Task NeverOffersTheFolderBeingComparedFrom()
    {
        // A folder against itself produces one column and an empty diff, which
        // reads as "these match" rather than as a mis-click.
        await Assert.That(DiffCandidates.Other(@"D:\Music\A", [@"D:\Music\A"])).IsEmpty();
    }

    [Test]
    public async Task TheSameFolderSpeltTwoWays_IsStillTheSameFolder()
    {
        // Two tabs can reach one folder by different spellings, and a drilldown
        // can land on a folder another tab is rooted at. The GUI ships win-x64
        // only, where paths are case-insensitive, and SetDiffRoots collapses
        // them for the same reason; offering it here would promise a comparison
        // the window then cannot make.
        var others = DiffCandidates.Other(@"D:\Music\A", [@"d:\music\a", @"D:\Music\B"]);
        await Assert.That(others).IsEquivalentTo(new[] { @"D:\Music\B" });
    }

    [Test]
    public async Task OneEntryPerFolder_HoweverManyTabsReachIt()
    {
        var others = DiffCandidates.Other(
            @"D:\Music\A", [@"D:\Music\B", @"D:\MUSIC\B", @"D:\Music\C"]);
        await Assert.That(others.Count).IsEqualTo(2);
    }

    [Test]
    public async Task NothingElseOpen_OffersNothing()
    {
        // The menu falls back to "Choose a folder…" here, so the feature stays
        // reachable; that is the window's job, not this rule's.
        await Assert.That(DiffCandidates.Other(@"D:\Music\A", [])).IsEmpty();
    }
}
