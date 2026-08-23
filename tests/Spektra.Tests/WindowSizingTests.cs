using Spektra.Core;

namespace Spektra.Tests;

/// The tool windows (Duplicate Detective, Folder Manifest) remember their size
/// but never reopen maximized, and a maximized size must never be mistaken for
/// the remembered one. Both rules exist because of one measured failure: a
/// window closed maximized had recorded the full-screen client size as its
/// "normal" size, reopened maximized AND with that size as its restore bounds,
/// and from then on un-maximizing produced a full-screen window with no real
/// size to come back to. The windows cannot be unit tested, so the rules live
/// in Core and are pinned here.
public class WindowSizingTests
{
    [Test]
    public async Task ARememberedSize_SurvivesUntouched()
    {
        await Assert.That(WindowSizing.ToolWindowRestore(981, 647, 2554, 1387))
            .IsEqualTo((981d, 647d));
    }

    [Test]
    public async Task AFullScreenRecord_IsCappedInsideTheScreen()
    {
        // The latched case: a placement recorded from a maximized window is the
        // whole working area give or take the frame. The cap is what un-latches
        // it, since the record itself says nothing about how it was made.
        await Assert.That(WindowSizing.ToolWindowRestore(2554, 1364, 2554, 1387))
            .IsEqualTo((2554 * 0.85, 1387 * 0.85));
    }

    [Test]
    public async Task ATinyRecord_IsRaisedToUsable()
    {
        await Assert.That(WindowSizing.ToolWindowRestore(120, 90, 2554, 1387))
            .IsEqualTo((400d, 300d));
    }

    [Test]
    public async Task ATinyScreen_DoesNotInvertTheClamp()
    {
        // On a work area whose 85% sits below the minimum, the minimum wins.
        // Math.Clamp throws outright when max < min, so without ordering the
        // guards this case is not a wrong answer but a crash at open.
        await Assert.That(WindowSizing.ToolWindowRestore(981, 647, 353, 217))
            .IsEqualTo((400d, 300d));
    }

    [Test]
    public async Task TheMaximizedClientSize_ReadsAsAGhost()
    {
        // Maximizing can race the size bookkeeping: the size-changed event can
        // fire while the state still reads Normal, so the maximized client size
        // lands in the normal-size field. At save time the window IS maximized,
        // so its client size is in hand to compare against.
        await Assert.That(WindowSizing.IsMaximizeGhost(2554, 1364, 2554, 1364)).IsTrue();
        // A whisker under still counts: DPI rounding shaves a pixel or two.
        await Assert.That(WindowSizing.IsMaximizeGhost(2551, 1361, 2554, 1364)).IsTrue();
        // Larger than the maximized client is equally impossible for a real
        // normal size (a stale record from a bigger monitor, for instance).
        await Assert.That(WindowSizing.IsMaximizeGhost(2560, 1369, 2554, 1364)).IsTrue();
    }

    [Test]
    public async Task ASizeSomeoneChose_IsNotAGhost()
    {
        await Assert.That(WindowSizing.IsMaximizeGhost(981, 647, 2554, 1364)).IsFalse();
        // Matching on one axis only is a tall or wide window, not a ghost.
        await Assert.That(WindowSizing.IsMaximizeGhost(2554, 647, 2554, 1364)).IsFalse();
        await Assert.That(WindowSizing.IsMaximizeGhost(981, 1364, 2554, 1364)).IsFalse();
    }
}
