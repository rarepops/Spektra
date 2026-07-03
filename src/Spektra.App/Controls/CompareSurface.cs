using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Runtime.InteropServices;
using Spektra.Core;

namespace Spektra.App.Controls;

public sealed class CompareSurface : Control
{
    private ComparisonViewModel? _vm;
    private readonly PaneRenderer _a = new();
    private readonly PaneRenderer _b = new();
    private WriteableBitmap? _diff;
    private readonly DispatcherTimer _tileTimer;
    private readonly DispatcherTimer _diffTimer;
    private bool _panning;
    private Point _lastPointer;

    public CompareSurface()
    {
        _tileTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tileTimer.Tick += (_, _) => { _tileTimer.Stop(); RenderTiles(); };
        _diffTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _diffTimer.Tick += (_, _) =>
        {
            _diffTimer.Stop();
            if (_vm?.Mode == CompareMode.Diff) _ = _vm.ComputeDiffAsync(Cols());
        };
    }

    private int Cols() => Math.Clamp((int)SpectrogramDraw.PlotRect(Bounds).Width, 64, 4096);

    public void Attach(ComparisonViewModel? vm)
    {
        if (_vm is not null)
        {
            _vm.A.DocumentChanged -= OnAChanged; _vm.A.DocumentUpdated -= OnAUpdated; _vm.A.TileChanged -= OnATile;
            _vm.B.DocumentChanged -= OnBChanged; _vm.B.DocumentUpdated -= OnBUpdated; _vm.B.TileChanged -= OnBTile;
            _vm.Viewport.Changed -= OnViewport;
            _vm.Changed -= OnVmChanged; _vm.DiffChanged -= OnDiffReady; _vm.DiffRequested -= OnDiffRequested;
        }
        _vm = vm;
        if (vm is not null)
        {
            vm.A.DocumentChanged += OnAChanged; vm.A.DocumentUpdated += OnAUpdated; vm.A.TileChanged += OnATile;
            vm.B.DocumentChanged += OnBChanged; vm.B.DocumentUpdated += OnBUpdated; vm.B.TileChanged += OnBTile;
            vm.Viewport.Changed += OnViewport;
            vm.Changed += OnVmChanged; vm.DiffChanged += OnDiffReady; vm.DiffRequested += OnDiffRequested;
            vm.TargetColumns = Cols();
            _a.SetDocument(vm.A.Document); _a.SetTile(vm.A.Tile);
            _b.SetDocument(vm.B.Document); _b.SetTile(vm.B.Tile);
        }
        InvalidateVisual();
    }

    private void OnAChanged() { _a.SetDocument(_vm?.A.Document); _a.SetTile(_vm?.A.Tile); ScheduleTiles(); InvalidateVisual(); }
    private void OnAUpdated() { _a.SyncColumns(); InvalidateVisual(); }
    private void OnATile() { _a.SetTile(_vm?.A.Tile); InvalidateVisual(); }
    private void OnBChanged() { _b.SetDocument(_vm?.B.Document); _b.SetTile(_vm?.B.Tile); ScheduleTiles(); InvalidateVisual(); }
    private void OnBUpdated() { _b.SyncColumns(); InvalidateVisual(); }
    private void OnBTile() { _b.SetTile(_vm?.B.Tile); InvalidateVisual(); }
    private void OnViewport() { if (_vm is not null) _vm.TargetColumns = Cols(); InvalidateVisual(); ScheduleTiles(); }
    private void OnVmChanged() => InvalidateVisual();
    private void OnDiffReady() { RebuildDiff(); InvalidateVisual(); }
    private void OnDiffRequested() { _diffTimer.Stop(); _diffTimer.Start(); }

    private void ScheduleTiles()
    {
        _tileTimer.Stop();
        if (_vm?.Viewport.IsTimeZoomed == true) _tileTimer.Start();
    }

    private void RenderTiles()
    {
        if (_vm?.Viewport.IsTimeZoomed != true) return;
        var cols = Cols();
        _ = _vm.A.RenderTileAsync(cols);
        _ = _vm.B.RenderTileAsync(cols);
    }

    // B pane mapping: shared time [t0,t1] over A's duration → B's own columns.
    private (double b0, double b1) BPaneRange(double t0, double t1)
    {
        if (_vm?.A.Metadata is not { } ma || _vm.B.Metadata is not { } mb || mb.Duration.TotalSeconds <= 0)
            return (t0, t1);
        var refDur = ma.Duration.TotalSeconds;
        var off = _vm.OffsetSeconds;
        return ((t0 * refDur + off) / mb.Duration.TotalSeconds,
                (t1 * refDur + off) / mb.Duration.TotalSeconds);
    }

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        if (_vm is null) return;
        var plot = SpectrogramDraw.PlotRect(Bounds);
        var vp = _vm.Viewport;
        var srA = _vm.A.Metadata?.SampleRate ?? 0;
        var durA = _vm.A.Metadata?.Duration.TotalSeconds ?? 0;

        switch (_vm.Mode)
        {
            case CompareMode.A:
                _a.DrawInto(ctx, plot, vp.T0, vp.T1, vp.F0, vp.F1, vp.T0, vp.T1);
                SpectrogramDraw.FrequencyRuler(ctx, plot, srA, vp.F0, vp.F1);
                SpectrogramDraw.TimeRuler(ctx, plot, durA, vp.T0, vp.T1);
                SpectrogramDraw.Legend(ctx, plot);
                break;
            case CompareMode.B:
            {
                var srB = _vm.B.Metadata?.SampleRate ?? 0;
                var (b0, b1) = BPaneRange(vp.T0, vp.T1);
                _b.DrawInto(ctx, plot, b0, b1, vp.F0, vp.F1, vp.T0, vp.T1);
                SpectrogramDraw.FrequencyRuler(ctx, plot, srB, vp.F0, vp.F1);
                SpectrogramDraw.TimeRuler(ctx, plot, durA, vp.T0, vp.T1);
                SpectrogramDraw.Legend(ctx, plot);
                break;
            }
            case CompareMode.Diff:
                RenderDiff(ctx, plot, vp);
                break;
            default: // Both
            {
                const double gap = 8;
                var half = (plot.Height - gap) / 2;
                var top = new Rect(plot.X, plot.Y, plot.Width, half);
                var bot = new Rect(plot.X, plot.Y + half + gap, plot.Width, half);
                _a.DrawInto(ctx, top, vp.T0, vp.T1, vp.F0, vp.F1, vp.T0, vp.T1);
                var (b0, b1) = BPaneRange(vp.T0, vp.T1);
                _b.DrawInto(ctx, bot, b0, b1, vp.F0, vp.F1, vp.T0, vp.T1);
                SpectrogramDraw.FrequencyRuler(ctx, top, srA, vp.F0, vp.F1);
                SpectrogramDraw.FrequencyRuler(ctx, bot, _vm.B.Metadata?.SampleRate ?? 0, vp.F0, vp.F1);
                SpectrogramDraw.TimeRuler(ctx, bot, durA, vp.T0, vp.T1); // one shared ruler under B
                SpectrogramDraw.Legend(ctx, plot);
                SpectrogramDraw.Text(ctx, "A", top.X + 4, top.Y + 2);
                SpectrogramDraw.Text(ctx, "B", bot.X + 4, bot.Y + 2);
                break;
            }
        }
    }

    private void RebuildDiff()
    {
        _diff?.Dispose();
        _diff = null;
        if (_vm?.Diff is not { Count: > 0 } cols) return;
        var bins = cols[0].Length;
        _diff = new WriteableBitmap(new PixelSize(cols.Count, bins),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = _diff.Lock();
        for (var col = 0; col < cols.Count; col++)
        {
            var data = cols[col];
            for (var bin = 0; bin < bins; bin++)
            {
                var row = bins - 1 - bin;
                Marshal.WriteInt32(fb.Address + row * fb.RowBytes + col * 4,
                    unchecked((int)DivergingPalette.ToBgra(data[bin], ComparisonViewModel.DiffClamp)));
            }
        }
    }

    private void RenderDiff(DrawingContext ctx, Rect plot, Viewport vp)
    {
        var srA = _vm!.A.Metadata?.SampleRate ?? 0;
        var durA = _vm.A.Metadata?.Duration.TotalSeconds ?? 0;
        if (_diff is not null && _vm.Diff is not null &&
            Math.Abs(_vm.DiffT0 - vp.T0) < 1e-9 && Math.Abs(_vm.DiffT1 - vp.T1) < 1e-9)
        {
            var src = new Rect(0, (1 - vp.F1) * _diff.PixelSize.Height,
                _diff.PixelSize.Width, Math.Max(1, vp.FreqSpanN * _diff.PixelSize.Height));
            ctx.DrawImage(_diff, src, plot);
        }
        SpectrogramDraw.FrequencyRuler(ctx, plot, srA, vp.F0, vp.F1);
        SpectrogramDraw.TimeRuler(ctx, plot, durA, vp.T0, vp.T1);
        SpectrogramDraw.DivergingLegend(ctx, plot, ComparisonViewModel.DiffClamp);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_vm is null) return;
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
        if (_vm is null) return;
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
