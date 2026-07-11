using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Spektra.Core;

namespace Spektra.App.Controls;

/// Shared pan/zoom/crosshair pointer handling for the plot surfaces. The single
/// document view and the compare surface drive the same Viewport identically:
/// the wheel zooms (Shift = frequency, otherwise time), a left drag pans,
/// double-click resets, and the pointer position is tracked for the crosshair.
/// The owner supplies which viewport to drive (returning null suppresses the
/// gesture, e.g. before a document has loaded) and where the plot sits.
internal sealed class PlotInteraction(Control owner, Func<Viewport?> viewport, Func<Rect> plotRect)
{
    private bool _panning;
    private Point _lastPointer;

    /// Last pointer position over the control, or null once it has left; the
    /// owner reads this to draw the crosshair.
    public Point? Cursor { get; private set; }

    public void WheelChanged(PointerWheelEventArgs e)
    {
        if (viewport() is not { } vp) return;
        var plot = plotRect();
        var p = e.GetPosition(owner);
        if (!plot.Contains(p)) return;
        var delta = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : e.Delta.X;
        if (delta == 0) return;
        var factor = delta > 0 ? 0.8 : 1.25;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            vp.ZoomFrequency(1 - (p.Y - plot.Top) / plot.Height, factor);
        else
            vp.ZoomTime((p.X - plot.Left) / plot.Width, factor);
        e.Handled = true;
    }

    public void Pressed(PointerPressedEventArgs e)
    {
        if (viewport() is not { } vp) return;
        var plot = plotRect();
        var p = e.GetPosition(owner);
        if (!plot.Contains(p)) return;
        if (!e.GetCurrentPoint(owner).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) { vp.Reset(); e.Handled = true; return; }
        _panning = true;
        _lastPointer = p;
        e.Pointer.Capture(owner);
        e.Handled = true;
    }

    public void Moved(PointerEventArgs e)
    {
        if (viewport() is not { } vp) return;
        var p = e.GetPosition(owner);
        Cursor = p;
        if (_panning)
        {
            var plot = plotRect();
            vp.PanTime(-(p.X - _lastPointer.X) / plot.Width * vp.TimeSpanN);
            vp.PanFrequency((p.Y - _lastPointer.Y) / plot.Height * vp.FreqSpanN);
            _lastPointer = p;
        }
        else owner.InvalidateVisual();
    }

    public void Exited()
    {
        Cursor = null;
        owner.InvalidateVisual();
    }

    public void Released(PointerReleasedEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        e.Pointer.Capture(null);
    }
}
