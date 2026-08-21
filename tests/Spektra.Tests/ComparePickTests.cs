using Spektra.Core;

namespace Spektra.Tests;

/// File > Compare used to refuse outright unless two documents were already
/// open, writing "Open at least two files to compare" to an 11px status line
/// while the menu item stayed enabled and kept its ellipsis. It now offers a
/// file picker instead, and a picker can come back with any number of files.
/// The branching lives here rather than in the window because Spektra.App has
/// no unit tests, so anything left in the click handler is verified by hand or
/// not at all.
public class ComparePickTests
{
    [Test]
    public async Task NoFiles_IsACancelledDialog_NotAnError()
    {
        // Dismissing a picker is the most common thing to do with one. Treating
        // it as a failure would put an error on screen for a deliberate choice.
        await Assert.That(ComparePick.Decide([])).IsEqualTo(ComparePick.Outcome.Cancelled);
    }

    [Test]
    public async Task TwoDistinctFiles_Compare()
    {
        await Assert.That(ComparePick.Decide([@"C:\a.flac", @"C:\b.mp3"]))
            .IsEqualTo(ComparePick.Outcome.Compare);
    }

    [Test]
    public async Task OneFile_AsksForTheSecond_RatherThanFailing()
    {
        // Clicking a single file and pressing Open is a reasonable reading of
        // "choose two files", so the answer is the other half, not a refusal.
        await Assert.That(ComparePick.Decide([@"C:\a.flac"]))
            .IsEqualTo(ComparePick.Outcome.NeedSecondFile);
    }

    [Test]
    public async Task ThreeOrMore_IsAmbiguous_AndSaysSo()
    {
        // Silently taking the first two would compare a pair the user never
        // named, and the wrong pair is worse than no pair.
        await Assert.That(ComparePick.Decide([@"C:\a.flac", @"C:\b.flac", @"C:\c.flac"]))
            .IsEqualTo(ComparePick.Outcome.TooMany);
        await Assert.That(ComparePick.Decide([@"C:\a", @"C:\b", @"C:\c", @"C:\d"]))
            .IsEqualTo(ComparePick.Outcome.TooMany);
    }

    [Test]
    public async Task TheSameFileTwice_IsRejected()
    {
        // Only reachable through the two-step flow, where the second picker
        // opens in the first file's folder with it sitting right there. A file
        // against itself produces an empty diff that looks like a verdict.
        await Assert.That(ComparePick.Decide([@"C:\a.flac", @"C:\a.flac"]))
            .IsEqualTo(ComparePick.Outcome.SameFileTwice);
    }

    [Test]
    public async Task TheSameFileTwice_IsCaughtRegardlessOfCase()
    {
        // The GUI ships win-x64 only and Windows paths are case-insensitive, so
        // two spellings of one path are one file. Ordinal comparison would let
        // this through and diff a file against itself.
        await Assert.That(ComparePick.Decide([@"C:\Music\A.flac", @"c:\music\a.FLAC"]))
            .IsEqualTo(ComparePick.Outcome.SameFileTwice);
    }
}
