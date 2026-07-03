using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Spektra.Core;

namespace Spektra.App.Controls;

public sealed class SpectrogramView : Control
{
    private DocumentViewModel? _vm;
    private readonly PaneRenderer _pane = new();
    private bool _panning;
    private Point _lastPointer;
    private readonly DispatcherTimer _tileTimer;

    public SpectrogramView()
    {
        _tileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tileTimer.Tick += (_, _) =>
        {
            _tileTimer.Stop();
            if (_vm is null) return;
            var columns = Math.Clamp((int)SpectrogramDraw.PlotRect(Bounds).Width, 64, 4096);
            _ = _vm.RenderTileAsync(columns);
        };
    }

    public void Attach(DocumentViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.DocumentChanged -= OnDocumentChanged;
            _vm.DocumentUpdated -= OnDocumentUpdated;
            _vm.TileChanged -= OnTileChanged;
            _vm.Viewport.Changed -= OnViewportChanged;
        }
        _vm = vm;
        if (vm is not null)
        {
            vm.DocumentChanged += OnDocumentChanged;
            vm.DocumentUpdated += OnDocumentUpdated;
            vm.TileChanged += OnTileChanged;
            vm.Viewport.Changed += OnViewportChanged;
        }
        _pane.SetDocument(vm?.Document);
        _pane.SetTile(vm?.Tile);
        ScheduleTile();
        InvalidateVisual();
    }

    private void OnViewportChanged() { InvalidateVisual(); ScheduleTile(); }

    private void ScheduleTile()
    {
        _tileTimer.Stop();
        if (_vm?.Viewport.IsTimeZoomed == true) _tileTimer.Start();
    }

    private void OnTileChanged() { _pane.SetTile(_vm?.Tile); InvalidateVisual(); }
    private void OnDocumentChanged() { _pane.SetDocument(_vm?.Document); _pane.SetTile(_vm?.Tile); ScheduleTile(); InvalidateVisual(); }
    private void OnDocumentUpdated() { _pane.SyncColumns(); InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var plot = SpectrogramDraw.PlotRect(Bounds);
        var vp = _vm?.Viewport;
        if (_vm?.Document is { } doc && vp is not null)
        {
            _pane.DrawInto(ctx, plot, vp.T0, vp.T1, vp.F0, vp.F1, vp.T0, vp.T1);
            SpectrogramDraw.FrequencyRuler(ctx, plot, doc.Metadata.SampleRate, vp.F0, vp.F1);
            SpectrogramDraw.TimeRuler(ctx, plot, doc.Metadata.Duration.TotalSeconds, vp.T0, vp.T1);
        }
        SpectrogramDraw.Legend(ctx, plot);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_vm?.Document is null) return;
        var plot = SpectrogramDraw.PlotRect(Bounds);
        var p = e.GetPosition(this);
        if (!plot.Contains(p)) return;
        var delta = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : e.Delta.X;
        if (delta == 0) return;
        var factor = delta > 0 ? 0.8 : 1.25;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            _vm.Viewport.ZoomFrequency(1 - (p.Y - plot.Top) / plot.Height, factor);
        else
            _vm.Viewport.ZoomTime((p.X - plot.Left) / plot.Width, factor);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm?.Document is null) return;
        var plot = SpectrogramDraw.PlotRect(Bounds);
        var p = e.GetPosition(this);
        if (!plot.Contains(p)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) { _vm.Viewport.Reset(); e.Handled = true; return; }
        _panning = true;
        _lastPointer = p;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_panning || _vm is null) return;
        var plot = SpectrogramDraw.PlotRect(Bounds);
        var p = e.GetPosition(this);
        var vp = _vm.Viewport;
        vp.PanTime(-(p.X - _lastPointer.X) / plot.Width * vp.TimeSpanN);
        vp.PanFrequency((p.Y - _lastPointer.Y) / plot.Height * vp.FreqSpanN);
        _lastPointer = p;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_panning) return;
        _panning = false;
        e.Pointer.Capture(null);
    }
}
