namespace Spektra.Core;

/// Sizing rules for the tool windows (Duplicate Detective, Folder Manifest):
/// remember the size, never the maximized state, and never trust a size that
/// is really the maximized one in disguise.
///
/// The failure these encode was measured, not imagined. Maximizing can race
/// the windows' size bookkeeping: the size-changed event can fire while the
/// state still reads Normal, so the maximized client size lands in the field
/// meant to hold the size to come back to. Closing then saved that as the
/// remembered size with the maximized flag set, reopening applied both, and
/// from there the record could never improve: while maximized nothing updates
/// the normal size, and un-maximizing handed back a full-screen "normal"
/// window. A tool window is transient besides, so reopening one maximized is
/// rarely what anyone meant; the main window keeps honouring its flag.
public static class WindowSizing
{
    /// The size a tool window reopens at: what was remembered, kept inside
    /// hard floors and a ceiling of most of the working area. The ceiling is
    /// what un-latches an already-poisoned record, since the record itself
    /// says nothing about how it was made. 0.85 rather than something nearer 1
    /// so the result reads as a window on the desktop, not a takeover.
    public static (double Width, double Height) ToolWindowRestore(
        double savedWidth, double savedHeight, double workWidth, double workHeight,
        double minWidth = 400, double minHeight = 300) =>
        // Ceiling first, floor last: on a work area whose 85% sits below the
        // minimum, Math.Clamp(value, min, max) with max < min throws, so the
        // minimum has to win by construction rather than by argument order.
        (Math.Max(minWidth, Math.Min(savedWidth, workWidth * 0.85)),
         Math.Max(minHeight, Math.Min(savedHeight, workHeight * 0.85)));

    /// True when a recorded "normal" size is really the maximized client size:
    /// at or above it on BOTH axes, with a small allowance for DPI rounding.
    /// Checked at save time, when the window is maximized and its client size
    /// is in hand. One axis alone is a tall or wide window, not a ghost.
    public static bool IsMaximizeGhost(
        double recordedWidth, double recordedHeight,
        double maximizedClientWidth, double maximizedClientHeight) =>
        recordedWidth >= maximizedClientWidth - 4
        && recordedHeight >= maximizedClientHeight - 4;
}
